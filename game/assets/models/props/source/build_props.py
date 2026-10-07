"""
Builds the salvage-style machines, tools and ores and exports one .glb per model to ../ (game/assets/models/props).

Run inside Blender after build_conveyors.py is on disk (its helpers are reused):
    exec(open(r"...\\props\\source\\build_props.py").read(), {"__name__": "__main__"})

Same conventions as the conveyors: Z up, +Y is Godot's -Z (a structure's front), albedo in the "Grime"
vertex colours, origin at the anchor cell's centre for structures. Objects that animate in Godot (flywheel,
rollers, beacon, spawner rings) are separate nodes whose origin sits on their pivot.
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix, noise

# resolved from this file when run by path (tools/blender/run.py); the fallback is for exec() from Blender
HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
        else r"C:\Users\steph\OneDrive\Documents\godot\projects\gmp-framework\game\assets\models\props\source")
OUT_DIR = os.path.dirname(HERE)
CONV = os.path.normpath(os.path.join(HERE, "..", "..", "conveyors", "source", "build_conveyors.py"))

L = {"__name__": "conveyor_lib", "__file__": CONV}
exec(compile(open(CONV, encoding="utf-8").read(), CONV, "exec"), L)
Builder, vc_mat, MATS, MAT_ORDER, MI = L["Builder"], L["vc_mat"], L["MATS"], L["MAT_ORDER"], L["MI"]
C_WHITE, C_DARK, C_FRAME, C_STEEL = L["C_WHITE"], L["C_DARK"], L["C_FRAME"], L["C_STEEL"]
C_YELLOW, C_BLACK, C_TAPE, C_AMBER, C_WOOD = L["C_YELLOW"], L["C_BLACK"], L["C_TAPE"], L["C_AMBER"], L["C_WOOD"]
set_active_colors, clear_collection = L["set_active_colors"], L["clear_collection"]

C_CYAN = (0.35, 0.95, 1.0)
C_COPPER = (0.8, 0.38, 0.16)
C_MOLTEN = (1.0, 0.36, 0.06)
C_MOSS = (0.10, 0.20, 0.05)

def mats():
    L["mats"]()
    extra = dict(
        glass=vc_mat("M_PropGlass", 0.05),
        cyan=vc_mat("M_PropCyanGlow", 0.3, 0.0, (0.3, 0.9, 1.0), 6.0),
        rock=vc_mat("M_OreRock", 0.88),
        gem=vc_mat("M_OreGem", 0.22, 0.35),
        copper=vc_mat("M_PropCopper", 0.32, 0.95),
        vortex=vc_mat("M_VoidVortex", 0.5),
        molten=vc_mat("M_PropMolten", 0.35, 0.0, (1.0, 0.32, 0.04), 3.5),
        lamp=vc_mat("M_PropLamp", 0.3, 0.0, (1.0, 0.96, 0.88), 5.0),
    )
    for k, m in extra.items():
        MATS[k] = m
        if k not in MAT_ORDER:
            MI[k] = len(MAT_ORDER); MAT_ORDER.append(k)

# ---------- extra geometry helpers ----------
def rot_to(axis):
    """Rotation taking +Z onto axis."""
    return Vector((0, 0, 1)).rotation_difference(Vector(axis).normalized()).to_matrix()

def ring(B, center, axis, r_in, r_out, width, seg, mat, c, var=0.12, rust=0.0):
    """Flat annulus band (a short thick tube) around axis."""
    R = rot_to(axis); ax = Vector(axis).normalized()
    pts = lambda r, d: [center + ax * d + R @ Vector((math.cos(a) * r, math.sin(a) * r, 0)) for a in [k / seg * math.tau for k in range(seg)]]
    outer = B.tube_rings([pts(r_out, -width / 2), pts(r_out, width / 2)], mat, c, var, rust, cap=False, smooth=True)
    inner = B.tube_rings([pts(r_in, width / 2), pts(r_in, -width / 2)], mat, c, var, rust, cap=False, smooth=True)
    return outer + inner

def rock(B, center, r, seed, subdiv=2, amp=0.28, mat="rock", color_fn=None, squash=(1, 1, 0.85)):
    """Faceted boulder: a displaced icosphere, flat shaded; color_fn(pos, normal) -> rgb per face."""
    res = bmesh.ops.create_icosphere(B.bm, subdivisions=subdiv, radius=r)
    vs = res["verts"]
    off = Vector((seed * 3.1, seed * 1.7, seed * 0.9))
    for v in vs:
        d = v.co.normalized()
        n = noise.noise(d * 1.6 + off) * amp + noise.noise(d * 4.0 + off) * amp * 0.35
        v.co = Vector((d.x * squash[0], d.y * squash[1], d.z * squash[2])) * r * (1 + n) + center
    fs = list({f for v in vs for f in v.link_faces})
    for f in fs:
        f.material_index = MI[mat]; f.smooth = False
    bmesh.ops.recalc_face_normals(B.bm, faces=fs)
    B.bm.normal_update()
    for f in fs:
        ctr = f.calc_center_median()
        col = color_fn(ctr, f.normal) if color_fn else (0.3, 0.3, 0.3)
        for l in f.loops:
            l[B.col] = (col[0], col[1], col[2], 1.0)
    return fs

def prism(B, p0, p1, r, sides=6, tip=0.35, mat="gem", c=(0.5, 0.5, 0.5), var=0.1):
    """Crystal: hexagonal prism from p0 toward p1 with a pointed tip (tip = fraction of length)."""
    ax = (p1 - p0); L_ = ax.length; ax.normalize()
    R = rot_to(ax)
    ring_ = lambda p, rr: [p + R @ Vector((math.cos(a) * rr, math.sin(a) * rr, 0)) for a in [k / sides * math.tau for k in range(sides)]]
    body_end = p0 + ax * L_ * (1 - tip)
    fs = B.tube_rings([ring_(p0, r), ring_(body_end, r), ring_(p1, r * 0.02)], mat, c, var, 0.0, cap=True, smooth=False)
    return fs

def hazard(B, origin, u, v, w, h, normal, pitch=0.09):
    """Black plate with slanted yellow stripes, on the plane origin + u*x + v*y, raised along normal."""
    u, v, n = Vector(u).normalized(), Vector(v).normalized(), Vector(normal).normalized()
    base = [B.bm.verts.new(origin + n * 0.001 + u * x + v * y) for x, y in ((0, 0), (w, 0), (w, h), (0, h))]
    f = B.bm.faces.new(base); f.material_index = MI["panel"]
    if f.normal.dot(n) < 0: f.normal_flip()
    B.paint([f], C_BLACK, 0.2)
    sk = h * 0.7
    def clip(poly, keep):                               # Sutherland-Hodgman against one x half-plane
        out = []
        for i, a in enumerate(poly):
            b = poly[(i + 1) % len(poly)]
            ina, inb = keep(a[0]), keep(b[0])
            if ina: out.append(a)
            if ina != inb:
                edge = 0.0 if (a[0] < 0) != (b[0] < 0) else w
                t = (edge - a[0]) / (b[0] - a[0])
                out.append((edge, a[1] + t * (b[1] - a[1])))
        return out
    k = 0
    while k * pitch < w + sk:
        x0 = k * pitch - sk
        poly = [(x0, 0), (x0 + pitch / 2, 0), (x0 + pitch / 2 + sk, h), (x0 + sk, h)]
        poly = clip(poly, lambda x: x >= 0)
        if len(poly) >= 3:
            poly = clip(poly, lambda x: x <= w)
        k += 1
        if len(poly) < 3:
            continue
        q = [B.bm.verts.new(origin + n * 0.002 + u * x + v * y) for x, y in poly]
        f = B.bm.faces.new(q); f.material_index = MI["panel"]
        if f.normal.dot(n) < 0: f.normal_flip()
        B.paint([f], C_YELLOW, 0.3)

def finish(B, name, coll, pivot=None):
    ob = B.to_object(name, coll)
    if pivot is not None:
        ob.location = pivot
    return ob

def uv_plane(name, coll, x0, x1, y0, y1, z, mat_key):
    bm = bmesh.new(); uvl = bm.loops.layers.uv.new("UVMap")
    vs = [bm.verts.new((x, y, z)) for x, y in ((x0, y0), (x1, y0), (x1, y1), (x0, y1))]
    f = bm.faces.new(vs)
    for l in f.loops:
        l[uvl].uv = ((l.vert.co.x - x0) / (x1 - x0), (l.vert.co.y - y0) / (y1 - y0))
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(MATS[mat_key])
    ob = bpy.data.objects.new(name, me); coll.objects.link(ob)
    return ob

def bolts(B, pts, axis, r=0.012, h=0.01):
    ax = Vector(axis).normalized()
    for p in pts:
        B.cyl(p, p + ax * h, r, 6, "steel", C_STEEL, rust=0.4)

# ======================================================================================
# Magnet tool (held; origin at the grip, barrel toward +Y = Godot -Z)
# ======================================================================================
def build_magnet():
    random.seed(5)
    coll = clear_collection("Prop_Magnet")
    B = Builder()
    tilt = Matrix.Rotation(math.radians(-14), 3, 'X')
    # pistol grip: dark rubber, amber trigger
    B.box(Vector((0, -0.03, -0.075)), (0.045, 0.065, 0.13), tilt, "rubber", C_BLACK, 0.1)
    for k in range(4):
        B.box(Vector((0, 0.004, -0.03 - k * 0.028)), (0.047, 0.006, 0.012), tilt, "rubber", (0.06, 0.06, 0.06), 0.1)
    B.box(Vector((0, 0.03, -0.025)), (0.016, 0.018, 0.04), tilt, "panel", C_AMBER, 0.1)
    # housing: a gutted facility power tool, white shell with a dark belly
    B.box(Vector((0, 0.03, 0.02)), (0.075, 0.2, 0.075), Matrix.Identity(3), "panel", C_WHITE, 0.3)
    B.box(Vector((0, 0.03, -0.012)), (0.078, 0.17, 0.02), Matrix.Identity(3), "metal", C_DARK, 0.2)
    for k in range(5):
        B.box(Vector((0.039, -0.02 + k * 0.022, 0.03)), (0.004, 0.012, 0.04), Matrix.Identity(3), "metal", (0.05, 0.05, 0.05), 0.1)  # vents
    # battery: facility cell pack strapped on top with tape
    B.box(Vector((0, -0.02, 0.085)), (0.065, 0.12, 0.05), Matrix.Identity(3), "panel", C_WHITE, 0.35)
    hazard(B, Vector((0.0326, -0.075, 0.066)), (0, 1, 0), (0, 0, 1), 0.11, 0.02, (1, 0, 0), pitch=0.02)
    B.box(Vector((0, 0.005, 0.085)), (0.072, 0.02, 0.058), Matrix.Identity(3), "tape", C_TAPE, 0.25)
    B.box(Vector((0, -0.055, 0.085)), (0.072, 0.018, 0.058), Matrix.Identity(3), "tape", C_TAPE, 0.25)
    B.box(Vector((0.02, 0.035, 0.112)), (0.012, 0.012, 0.006), Matrix.Identity(3), "glow", C_AMBER, 0.05)
    # barrel: iron core through a hand-wound copper coil between two flanges
    ax = Vector((0, 1, 0))
    B.cyl(Vector((0, 0.12, 0.02)), Vector((0, 0.44, 0.02)), 0.022, 10, "steel", (0.22, 0.22, 0.23), rust=0.3)
    B.cyl(Vector((0, 0.165, 0.02)), Vector((0, 0.355, 0.02)), 0.043, 16, "copper", C_COPPER, 0.15)
    for k in range(13):                                        # winding grooves
        y = 0.172 + k * 0.0145
        ring(B, Vector((0, y, 0.02)), ax, 0.041, 0.0445, 0.003, 16, "copper", (0.45, 0.2, 0.08), 0.1)
    for y in (0.16, 0.36):
        B.cyl(Vector((0, y - 0.006, 0.02)), Vector((0, y + 0.006, 0.02)), 0.056, 16, "metal", C_DARK, 0.15)
    # pole piece: a flared iron shoe with a ring of cyan field LEDs
    pole = lambda y, r: [Vector((math.cos(a) * r, y, 0.02 + math.sin(a) * r)) for a in [k / 16 * math.tau for k in range(16)]]
    B.tube_rings([pole(0.38, 0.03), pole(0.425, 0.068), pole(0.46, 0.068)], "metal", (0.1, 0.1, 0.11), 0.15, 0.2, cap=True, smooth=True)
    for k in range(8):
        a = k / 8 * math.tau
        p = Vector((math.cos(a) * 0.052, 0.461, 0.02 + math.sin(a) * 0.052))
        B.cyl(p, p + Vector((0, 0.004, 0)), 0.006, 6, "cyan", C_CYAN, 0.05)
    # cooling fins between housing and coil
    for k in range(4):
        y = 0.13 + k * 0.009
        B.cyl(Vector((0, y, 0.02)), Vector((0, y + 0.003, 0.02)), 0.05, 12, "steel", C_STEEL, 0.1, rust=0.2)
    # cables: battery to the coil's rear flange, one looping under the barrel
    B.pipe([Vector((0.02, 0.04, 0.09)), Vector((0.035, 0.12, 0.075)), Vector((0.035, 0.16, 0.05))], 0.006, 5)
    B.pipe([Vector((-0.02, -0.07, 0.07)), Vector((-0.045, 0.02, 0.0)), Vector((-0.04, 0.12, -0.02)), Vector((-0.03, 0.16, 0.0))], 0.006, 5)
    finish(B, "Body", coll)
    # the coil's glow shell, shown only while the magnet is pulling (MagnetRod.activeIndicator)
    G = Builder()
    G.cyl(Vector((0, 0.166, 0.02)), Vector((0, 0.354, 0.02)), 0.0465, 16, "cyan", C_CYAN, 0.05, cap=False)
    G.cyl(Vector((0, 0.4605, 0.02)), Vector((0, 0.462, 0.02)), 0.045, 16, "cyan", C_CYAN, 0.05)
    finish(G, "CoilGlow", coll)
    return coll

# ======================================================================================
# Item crate (default held / dropped box): 1 m specimen container, origin at its centre
# ======================================================================================
def build_crate():
    random.seed(8)
    coll = clear_collection("Prop_Crate")
    B = Builder()
    I3 = Matrix.Identity(3)
    B.box(Vector((0, 0, 0)), (0.94, 0.94, 0.94), I3, "panel", C_WHITE, 0.28)
    e = 0.47
    for (ax, a, b) in (("x", 1, 2), ("y", 0, 2), ("z", 0, 1)):
        for sa in (-1, 1):
            for sb in (-1, 1):
                c = [0, 0, 0]; c[a] = sa * e; c[b] = sb * e
                size = [0.06, 0.06, 0.06]; size["xyz".index(ax)] = 0.9
                B.box(Vector(c), size, I3, "metal", C_FRAME, 0.2, rust=0.3)
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                B.box(Vector((sx * 0.465, sy * 0.465, sz * 0.465)), (0.075, 0.075, 0.075), I3, "rubber", (0.12, 0.1, 0.05) if sz > 0 else C_BLACK, 0.2)
    # each side: a recessed dark plate under the icon (M*) and a stencil strip under the name (L*)
    for n in (Vector((0, -1, 0)), Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((-1, 0, 0))):
        R = Matrix((Vector((0, 0, 1)).cross(n), Vector((0, 0, 1)), n)).transposed()   # local x across, y up, z out
        B.box(n * 0.471 + Vector((0, 0, -0.06)), (0.56, 0.56, 0.006), R, "metal", (0.06, 0.06, 0.065), 0.15)
        B.box(n * 0.471 + Vector((0, 0, 0.33)), (0.78, 0.13, 0.006), R, "metal", (0.05, 0.05, 0.055), 0.1)
        for sx in (-1, 1):
            for sy in (-1, 1):
                B.cyl(n * 0.474 + R @ Vector((sx * 0.25, -0.06 + sy * 0.25, 0)), n * 0.482 + R @ Vector((sx * 0.25, -0.06 + sy * 0.25, 0)), 0.01, 6, "steel", C_STEEL, rust=0.4)
    # hazard band round the lid seam, two carry handles, a latch and a status LED on top
    for n in (Vector((0, -1, 0)), Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((-1, 0, 0))):
        side = Vector((0, 0, 1)).cross(n)
        hazard(B, n * 0.4705 - side * 0.39 + Vector((0, 0, -0.43)), side, (0, 0, 1), 0.78, 0.05, n, pitch=0.06)
    for sx in (-1, 1):
        B.pipe([Vector((sx * 0.3, -0.18, 0.47)), Vector((sx * 0.3, -0.15, 0.53)), Vector((sx * 0.3, 0.15, 0.53)), Vector((sx * 0.3, 0.18, 0.47))], 0.015, 6, "steel", C_STEEL)
    B.box(Vector((0, -0.4, 0.475)), (0.1, 0.05, 0.02), I3, "glow", C_AMBER, 0.05)
    B.box(Vector((0, 0.38, 0.47)), (0.3, 0.06, 0.03), I3, "tape", C_TAPE, 0.25)
    finish(B, "Crate", coll)
    return coll

# ======================================================================================
# Grinder: 1 x 2 cells (origin = bottom cell centre). Solid base, hopper in the upper cell with a low intake
# lip at the back (-Y) so an upper-level conveyor can feed it; shredder rollers, flywheel, beacon.
# ======================================================================================
def build_grinder():
    random.seed(13)
    coll = clear_collection("Prop_Grinder")
    B = Builder(); I3 = Matrix.Identity(3)
    # base housing: dark frame with salvaged white cladding
    B.box(Vector((0, 0, -0.05)), (1.84, 1.84, 1.9), I3, "metal", C_FRAME, 0.2, rust=0.3)
    for sx in (-1, 1):
        for (y0, y1, z0, z1, col) in ((-0.85, -0.05, -0.75, 0.2, C_WHITE), (0.02, 0.85, -0.75, 0.55, C_WHITE),
                                      (-0.85, 0.2, 0.28, 0.82, (0.6, 0.6, 0.58)), (0.3, 0.85, 0.62, 0.82, C_DARK)):
            B.box(Vector((sx * 0.925, (y0 + y1) / 2, (z0 + z1) / 2)), (0.02, y1 - y0, z1 - z0), I3, "panel", col, 0.3)
    B.box(Vector((0, 0, -0.97)), (1.96, 1.96, 0.06), I3, "steel", C_STEEL, 0.25, rust=0.7)                    # base skid
    B.box(Vector((0, 0, 0.95)), (1.98, 1.98, 0.1), I3, "metal", C_DARK, 0.2, rust=0.3)                        # deck under the hopper
    # front: hazard apron and the output mouth at belt height
    hazard(B, Vector((-0.8, 0.921, -0.95)), (1, 0, 0), (0, 0, 1), 1.6, 0.12, (0, 1, 0))
    B.box(Vector((0, 0.9, -0.35)), (0.9, 0.08, 0.5), I3, "metal", (0.02, 0.02, 0.02), 0.05)                  # dark mouth
    for sx in (-1, 1):
        B.box(Vector((sx * 0.5, 0.94, -0.35)), (0.08, 0.1, 0.62), I3, "steel", C_AMBER, 0.2, rust=0.2)
    B.box(Vector((0, 0.94, -0.03)), (1.08, 0.1, 0.08), I3, "steel", C_AMBER, 0.2, rust=0.2)
    B.box(Vector((0, 0.93, -0.62)), (0.92, 0.12, 0.03), Matrix.Rotation(math.radians(-18), 3, 'X'), "steel", C_STEEL, 0.2, rust=0.4)  # chute lip
    # front upper: a salvaged facility control panel (white, dim screen, three lit buttons) and a vent grille
    B.box(Vector((-0.4, 0.93, 0.5)), (0.95, 0.03, 0.66), I3, "panel", C_WHITE, 0.3)
    B.box(Vector((-0.52, 0.948, 0.58)), (0.5, 0.01, 0.3), I3, "metal", (0.02, 0.03, 0.03), 0.05)
    for k in range(4):
        B.box(Vector((-0.62 + k * 0.07, 0.954, 0.52 + (k % 2) * 0.06)), (0.05, 0.004, 0.012), I3, "glow", C_AMBER, 0.05)
    for k, col in enumerate((C_AMBER, C_CYAN, (1.0, 0.15, 0.1))):
        B.cyl(Vector((-0.12, 0.945, 0.7 - k * 0.12)), Vector((-0.12, 0.965, 0.7 - k * 0.12)), 0.035, 10, "glow", col, 0.05)
    B.box(Vector((-0.4, 0.95, 0.25)), (0.8, 0.012, 0.1), I3, "tape", C_TAPE, 0.25)
    for k in range(8):
        B.box(Vector((0.5, 0.93, 0.2 + k * 0.08)), (0.6, 0.04, 0.025), I3, "metal", (0.05, 0.05, 0.05), 0.1, rust=0.3)
    # hopper walls (collision: +-0.925 x 0.15 thick, z 1.0..2.2; back wall is a lip to z 1.12)
    for sx in (-1, 1):
        B.box(Vector((sx * 0.925, 0, 1.6)), (0.13, 1.98, 1.2), I3, "steel", (0.3, 0.3, 0.3), 0.25, rust=0.6)
        B.box(Vector((sx * 0.855, 0, 1.55)), (0.012, 1.7, 1.0), I3, "steel", L["C_WEAR"], 0.12, rust=0.1)       # wear liner
        bolts(B, [Vector((sx * 0.991, y, z)) for y in (-0.8, -0.3, 0.3, 0.8) for z in (1.15, 2.05)], (sx, 0, 0))
    B.box(Vector((0, 0.925, 1.6)), (1.98, 0.13, 1.2), I3, "steel", (0.3, 0.3, 0.3), 0.25, rust=0.6)            # front wall
    B.box(Vector((0, -0.925, 1.06)), (1.98, 0.13, 0.12), I3, "steel", C_STEEL, 0.2, rust=0.5)                   # intake lip
    hazard(B, Vector((-0.85, -0.991, 1.005)), (1, 0, 0), (0, 0, 1), 1.7, 0.11, (0, -1, 0), pitch=0.08)
    for sx in (-1, 1):                                                                                          # intake posts
        B.box(Vector((sx * 0.925, -0.925, 1.6)), (0.14, 0.14, 1.2), I3, "metal", C_DARK, 0.2, rust=0.3)
    # rolled rim with a hazard band all round the top
    for (c, s) in ((Vector((0, 0.925, 2.2)), (1.98, 0.16, 0.06)), (Vector((-0.925, 0, 2.2)), (0.16, 1.98, 0.06)),
                   (Vector((0.925, 0, 2.2)), (0.16, 1.98, 0.06)), (Vector((0, -0.925, 2.2)), (1.98, 0.16, 0.06))):
        B.box(c, s, I3, "metal", C_DARK, 0.2, rust=0.4)
    hazard(B, Vector((-0.95, 0.991, 1.95)), (1, 0, 0), (0, 0, 1), 1.9, 0.2, (0, 1, 0), pitch=0.12)
    for sx in (-1, 1):
        hazard(B, Vector((sx * 0.991, -0.95 if sx > 0 else 0.95, 1.95)), (0, 1 if sx > 0 else -1, 0), (0, 0, 1), 1.9, 0.2, (sx, 0, 0), pitch=0.12)
    # roller bearing blocks on the hopper walls
    for sx in (-1, 1):
        for y in (-0.26, 0.26):
            B.box(Vector((sx * 0.8, y, 1.15)), (0.1, 0.16, 0.16), I3, "metal", C_DARK, 0.2, rust=0.3)
    # drive: gearmotor on the right, V-belt up to the flywheel
    B.box(Vector((0.8, -0.55, -0.62)), (0.26, 0.34, 0.26), I3, "metal", (0.16, 0.2, 0.26), 0.25, rust=0.15)
    B.cyl(Vector((0.94, -0.55, -0.62)), Vector((0.99, -0.55, -0.62)), 0.07, 12, "steel", C_STEEL, rust=0.3)
    B.box(Vector((0.8, -0.55, -0.44)), (0.12, 0.2, 0.08), I3, "panel", C_WHITE, 0.3)
    B.box(Vector((0.8, -0.48, -0.395)), (0.03, 0.03, 0.012), I3, "glow", C_AMBER, 0.05)
    fw = Vector((0.955, 0.15, -0.1))
    B.pipe([Vector((0.965, -0.55, -0.55)), fw + Vector((0.01, -0.1, 0.4)), fw + Vector((0.01, 0.2, 0.42))], 0.014, 6)
    B.pipe([Vector((0.965, -0.55, -0.69)), fw + Vector((0.01, -0.1, -0.44)), fw + Vector((0.01, 0.25, -0.4))], 0.014, 6)
    B.cyl(Vector((0.89, fw.y, fw.z)), Vector((0.93, fw.y, fw.z)), 0.1, 12, "metal", C_DARK, rust=0.2)          # flywheel bearing
    finish(B, "Body", coll)
    # flywheel (spins about its axle, X)
    F = Builder()
    ring(F, Vector((0, 0, 0)), (1, 0, 0), 0.44, 0.52, 0.05, 28, "steel", (0.26, 0.26, 0.27), 0.2, rust=0.5)
    F.cyl(Vector((-0.025, 0, 0)), Vector((0.03, 0, 0)), 0.08, 12, "metal", C_DARK, rust=0.2)
    for k in range(6):
        a = k / 6 * math.tau
        d = Vector((0, math.cos(a), math.sin(a)))
        F.box(d * 0.26, (0.03, 0.36, 0.05), Matrix.Rotation(a, 3, 'X'), "steel", (0.26, 0.26, 0.27), 0.2, rust=0.4)
    F.box(Vector((0.02, 0, 0.47)), (0.02, 0.12, 0.03), Matrix.Identity(3), "panel", C_YELLOW, 0.2)              # balance mark
    finish(F, "Flywheel", coll, fw)
    # shredder rollers: toothed drums across the hopper floor, counter-rotating (about X)
    for name, y, phase in (("RollerA", -0.26, 0.0), ("RollerB", 0.26, 0.3)):
        R_ = Builder()
        R_.cyl(Vector((-0.74, 0, 0)), Vector((0.74, 0, 0)), 0.15, 14, "steel", (0.2, 0.2, 0.21), 0.2, rust=0.5)
        for i in range(9):
            x = -0.66 + i * 0.165
            for k in range(6):
                a = k / 6 * math.tau + phase + i * 0.5
                d = Vector((0, math.cos(a), math.sin(a)))
                R_.box(Vector((x, 0, 0)) + d * 0.17, (0.05, 0.05, 0.1), Matrix.Rotation(a, 3, 'X'), "steel", L["C_WEAR"], 0.15, rust=0.3)
        finish(R_, name, coll, Vector((0, y, 1.15)))
    # rotating warning beacon on the front-left hopper corner
    Bc = Builder()
    Bc.cyl(Vector((-0.86, 0.86, 2.23)), Vector((-0.86, 0.86, 2.27)), 0.07, 12, "metal", C_DARK)
    Bc.cyl(Vector((-0.86, 0.86, 2.27)), Vector((-0.86, 0.86, 2.4)), 0.06, 12, "glow", C_AMBER, 0.05)
    finish(Bc, "BeaconBase", coll)
    Rf = Builder()
    Rf.box(Vector((0, 0.02, 0)), (0.07, 0.01, 0.09), Matrix.Identity(3), "steel", (0.8, 0.8, 0.8), 0.05)
    finish(Rf, "Beacon", coll, Vector((-0.86, 0.86, 2.335)))
    return coll

# ======================================================================================
# Spawner: a salvaged facility spawn tube on a plinth (1 cell, origin = cell centre); output chute at the front
# ======================================================================================
def build_spawner():
    random.seed(21)
    coll = clear_collection("Prop_Spawner")
    B = Builder(); I3 = Matrix.Identity(3)
    B.box(Vector((0, 0, -0.68)), (1.8, 1.8, 0.64), I3, "metal", C_FRAME, 0.2, rust=0.3)
    for n in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, -1, 0))):
        side = Vector((0, 0, 1)).cross(n)
        R = Matrix((side, Vector((0, 0, 1)), n)).transposed()
        B.box(n * 0.905 + Vector((0, 0, -0.62)), (1.5, 0.4, 0.02), R, "panel", C_WHITE, 0.3)
        hazard(B, n * 0.901 - side * 0.85 + Vector((0, 0, -0.99)), side, (0, 0, 1), 1.7, 0.08, n, pitch=0.1)
    # output chute at belt height through the front
    B.box(Vector((0, 0.9, -0.65)), (0.9, 0.06, 0.46), I3, "metal", (0.02, 0.02, 0.02), 0.05)
    for sx in (-1, 1):
        B.box(Vector((sx * 0.5, 0.93, -0.65)), (0.08, 0.08, 0.56), I3, "steel", C_CYAN, 0.1)
    B.box(Vector((0, 0.93, -0.39)), (1.08, 0.08, 0.06), I3, "steel", (0.9, 0.9, 0.88), 0.2)
    B.box(Vector((0, 0.95, -0.86)), (0.92, 0.12, 0.03), Matrix.Rotation(math.radians(-15), 3, 'X'), "steel", C_STEEL, 0.2, rust=0.3)
    # tube: steel collar rings top and bottom, cyan-lit glass between
    ring(B, Vector((0, 0, -0.33)), (0, 0, 1), 0.5, 0.7, 0.06, 32, "metal", C_DARK, 0.2, rust=0.2)
    ring(B, Vector((0, 0, -0.29)), (0, 0, 1), 0.62, 0.66, 0.04, 32, "cyan", C_CYAN, 0.05)
    ring(B, Vector((0, 0, 0.72)), (0, 0, 1), 0.5, 0.7, 0.08, 32, "metal", C_DARK, 0.2, rust=0.2)
    for k in range(6):                                          # tie rods
        a = k / 6 * math.tau + 0.3
        p = Vector((math.cos(a) * 0.68, math.sin(a) * 0.68, 0))
        B.cyl(p + Vector((0, 0, -0.3)), p + Vector((0, 0, 0.7)), 0.018, 6, "steel", C_STEEL, rust=0.4)
    # cap: dark dome housing with a white band, vents and a cable bundle down the back
    capr = lambda z, r: [Vector((math.cos(a) * r, math.sin(a) * r, z)) for a in [k / 24 * math.tau for k in range(24)]]
    B.tube_rings([capr(0.76, 0.7), capr(0.86, 0.66), capr(0.93, 0.5), capr(0.96, 0.25)], "metal", C_FRAME, 0.2, 0.3, cap=True, smooth=True)
    ring(B, Vector((0, 0, 0.8)), (0, 0, 1), 0.66, 0.705, 0.05, 32, "panel", C_WHITE, 0.3)
    B.box(Vector((0, 0, 0.975)), (0.3, 0.3, 0.04), I3, "metal", C_DARK, 0.2)
    B.box(Vector((0.1, 0.1, 1.0)), (0.05, 0.05, 0.01), I3, "glow", C_AMBER, 0.05)
    for dx in (-0.05, 0.0, 0.05):
        B.pipe([Vector((dx, -0.55, 0.88)), Vector((dx, -0.8, 0.75)), Vector((dx * 1.5, -0.86, 0.2)), Vector((dx * 2, -0.86, -0.34))], 0.018, 6)
    finish(B, "Body", coll)
    G = Builder()
    tube = lambda z: [Vector((math.cos(a) * 0.62, math.sin(a) * 0.62, z)) for a in [k / 32 * math.tau for k in range(32)]]
    G.tube_rings([tube(-0.3), tube(0.72)], "glass", (0.55, 0.9, 1.0), 0.02, 0.0, cap=False, smooth=True)
    finish(G, "Glass", coll)
    # containment rings (spin about Z) and the phantom core they hold
    Rg = Builder()
    for z, r, tilt in ((-0.1, 0.46, 0.25), (0.2, 0.5, -0.3), (0.5, 0.44, 0.15)):
        tube_ = [Vector((math.cos(a) * r, math.sin(a) * r, math.sin(a) * tilt * 0.4)) for a in [k / 32 * math.tau for k in range(32)]]
        rr = [Matrix.Translation(Vector((0, 0, z))) @ p for p in tube_]
        Rg.pipe(rr + [rr[0]], 0.012, 5, "cyan", C_CYAN)
    finish(Rg, "Rings", coll, Vector((0, 0, 0.0)))
    Cr = Builder()
    rock(Cr, Vector((0, 0, 0)), 0.17, 4.0, 1, 0.3, "cyan", lambda p, n: (0.25, 0.85, 1.0))
    finish(Cr, "Core", coll, Vector((0, 0, 0.2)))
    return coll

# ======================================================================================
# Void: 2 x 2 cells (origin = anchor cell centre; footprint x -1..3, y -1..3). Vortex floor, three 1 m walls
# and a low intake lip at the back (-Y) so a ground-level conveyor feeds straight in.
# ======================================================================================
def build_void():
    random.seed(34)
    coll = clear_collection("Prop_Void")
    B = Builder(); I3 = Matrix.Identity(3)
    cx, cy = 1.0, 1.0
    B.box(Vector((cx, cy, -0.985)), (3.98, 3.98, 0.03), I3, "metal", (0.03, 0.03, 0.035), 0.1)               # floor pan under the vortex
    # walls: stacked salvaged panels on dark posts (W, E, F), 1 m tall
    walls = [(Vector((-0.925, cy, -0.5)), (0.14, 3.98, 1.0), (-1, 0, 0)), (Vector((2.925, cy, -0.5)), (0.14, 3.98, 1.0), (1, 0, 0)),
             (Vector((cx, 2.925, -0.5)), (3.98, 0.14, 1.0), (0, 1, 0))]
    for c, s, n in walls:
        B.box(c, s, I3, "metal", (0.1, 0.1, 0.11), 0.25, rust=0.5)
        n = Vector(n); side = Vector((0, 0, 1)).cross(n)
        span = 3.7
        k = 0.0
        while k < span - 0.05:
            w = min(span - k, random.uniform(0.7, 1.3))
            col = random.choice([C_WHITE, C_WHITE, (0.6, 0.6, 0.58), C_DARK])
            h = random.uniform(0.62, 0.8)
            p = c + n * 0.075 + side * (-span / 2 + k + w / 2) + Vector((0, 0, -0.5 + 0.08 + h / 2 + 0.5 - 0.5))
            R = Matrix((side, Vector((0, 0, 1)), n)).transposed()
            B.box(Vector((p.x, p.y, -0.92 + h / 2)), (w - 0.03, h, 0.015), R, "panel", col, 0.3, rust=0.15)
            k += w
        hazard(B, c + n * 0.071 - side * 1.99 + Vector((0, 0, 0.36)), side, (0, 0, 1), 3.98, 0.12, n, pitch=0.14)
        B.box(c + Vector((0, 0, 0.51)), (s[0] + 0.02, s[1] + 0.02, 0.04), I3, "metal", C_DARK, 0.2, rust=0.4)   # capping
        # inner glow strip just above the vortex
        B.box(c - n * 0.072 + Vector((0, 0, -0.88)), (s[0] if s[0] > 1 else 0.01, s[1] if s[1] > 1 else 0.01, 0.025), I3, "cyan", (0.55, 0.3, 1.0), 0.05)
    # intake lip at the back: low hazard-chevron plate just under belt height
    B.box(Vector((cx, -0.925, -0.94)), (3.98, 0.14, 0.12), I3, "steel", C_STEEL, 0.2, rust=0.5)
    hazard(B, Vector((-0.99, -0.996, -0.995)), (1, 0, 0), (0, 0, 1), 3.98, 0.1, (0, -1, 0), pitch=0.12)
    # corner posts up to an intake gantry with warning lamps (well above anything riding in)
    for (x, y) in ((-0.93, -0.93), (2.93, -0.93), (-0.93, 2.93), (2.93, 2.93)):
        top = 0.95 if y < 0 else 0.1
        B.box(Vector((x, y, (-1 + top) / 2)), (0.12, 0.12, 1 + top), I3, "metal", C_DARK, 0.2, rust=0.4)
    B.box(Vector((cx, -0.93, 0.95)), (3.98, 0.12, 0.14), I3, "metal", C_DARK, 0.2, rust=0.4)
    hazard(B, Vector((-0.99, -0.992, 0.885)), (1, 0, 0), (0, 0, 1), 3.98, 0.13, (0, -1, 0), pitch=0.14)
    for x in (0.0, 2.0):
        B.cyl(Vector((x, -0.93, 0.88)), Vector((x, -0.93, 0.8)), 0.06, 10, "glow", C_AMBER, 0.05)
    B.pipe([Vector((-0.93, -0.86, 0.9)), Vector((0.5, -0.85, 0.82)), Vector((1.5, -0.85, 0.84)), Vector((2.93, -0.86, 0.9))], 0.012, 5)
    finish(B, "Body", coll)
    uv_plane("Vortex", coll, -0.86, 2.86, -0.86, 2.86, -0.965, "vortex")
    return coll

# ======================================================================================
# Smelter: 2 x 2 x 2 cells (origin = anchor cell centre; footprint x -1..3, y -1..3, z -1..3). A furnace rebuilt
# from facility wall panels: panelled lower housing with the output mouth at belt height in front of the anchor
# column (+Y); a hopper boxed in on all four sides across the back of the upper level, so items are dropped in from
# above; a panelled kiln with a molten seam and a chimney over the front half.
# ======================================================================================
SM_C = Vector((1.0, 1.0, 0.0))                      # footprint centre (x, y)
SM_FACES = {"F": (Vector((0, 1, 0)), Vector((1, 0, 0))), "B": (Vector((0, -1, 0)), Vector((1, 0, 0))),
            "R": (Vector((1, 0, 0)), Vector((0, 1, 0))), "L": (Vector((-1, 0, 0)), Vector((0, 1, 0)))}
SM_ROWS = ((-0.92, 0.0), (0.0, 0.95))                # lower-housing panel rows (z)

def sm_point(f, s, z, d):
    """Point on housing face f: s = world coordinate along the face, d = distance out from the footprint centre."""
    n, u = SM_FACES[f]
    return SM_C + n * d + u * (s - 1.0) + Vector((0, 0, z))

def sm_basis(f):
    n, u = SM_FACES[f]
    return Matrix((u, Vector((0, 0, 1)), n)).transposed()      # local x along the face, y up, z out

def facility_panel(B, f, s0, z0, z1, kind):
    """One salvaged 1 m facility panel in the housing cell starting at s0 (z0..z1).
    kind: white / dark / patched / gone (panel missing: cooling fins with the furnace glowing between them)."""
    R = sm_basis(f); n, _ = SM_FACES[f]
    c = sm_point(f, s0 + 0.5, (z0 + z1) / 2, 1.955)
    w, h = 0.93, (z1 - z0) - 0.07
    if kind == "white":
        B.box(c, (w, h, 0.04), R, "panel", C_WHITE, 0.3)
    elif kind == "dark":
        B.box(c, (w, h, 0.04), R, "metal", C_DARK, 0.2)
    elif kind == "patched":                                       # rusty steel sheet bolted over the hole
        B.box(c, (w * 0.85, h * 0.8, 0.02), R, "steel", C_STEEL, 0.25, rust=0.8)
        bolts(B, [c + R @ Vector((sx * w * 0.38, sy * h * 0.34, 0.01)) for sx in (-1, 1) for sy in (-1, 1)], n)
    else:
        B.box(c - n * 0.03, (w, h, 0.01), R, "molten", C_MOLTEN, 0.05)
        for k in range(5):
            B.box(c + R @ Vector((0, -h / 2 + (k + 0.5) * h / 5, -0.005)), (w, 0.035, 0.05), R, "metal", C_DARK, 0.15, rust=0.3)

def build_smelter():
    random.seed(71); rng = random.Random(71)
    coll = clear_collection("Prop_Smelter")
    B = Builder(); I3 = Matrix.Identity(3); Z = Vector((0, 0, 1))

    # ---- lower housing: dark frame box, base skid, deck, facility panels on a 1 m grid ----
    B.box(Vector((1, 1, 0.015)), (3.84, 3.84, 1.87), I3, "metal", C_FRAME, 0.2, rust=0.3)
    B.box(Vector((1, 1, -0.96)), (3.98, 3.98, 0.08), I3, "steel", C_STEEL, 0.25, rust=0.7)
    B.box(Vector((1, 1, 1.0)), (3.98, 3.98, 0.1), I3, "metal", C_DARK, 0.2, rust=0.3)
    for (x, y) in ((-0.94, -0.94), (2.94, -0.94), (-0.94, 2.94), (2.94, 2.94)):                 # corner posts
        B.box(Vector((x, y, 0.0)), (0.1, 0.1, 1.84), I3, "metal", C_FRAME, 0.2, rust=0.4)
    reserved = {("F", -1, 0), ("F", 0, 0), ("F", 1, 0), ("F", 2, 0), ("B", 0, 0)}
    forced = {("R", 1, 0): "gone", ("L", 0, 1): "gone", ("B", 2, 1): "gone", ("R", -1, 1): "patched",
              ("F", 2, 1): "dark", ("R", 0, 1): "white"}
    for f in SM_FACES:
        R = sm_basis(f)
        for s in (0, 1, 2):                                                                      # vertical frame bars
            for row, (z0, z1) in enumerate(SM_ROWS):
                if (f, s - 1, row) in reserved and (f, s, row) in reserved:
                    continue                                                                     # inside a fitted plate
                B.box(sm_point(f, s, (z0 + z1) / 2, 1.96), (0.07, z1 - z0, 0.06), R, "metal", C_FRAME, 0.2, rust=0.3)
        for z in (-0.92, 0.0, 0.93):                                                             # horizontal bars
            B.box(sm_point(f, 1.0, z, 1.96), (3.8, 0.07, 0.06), R, "metal", C_FRAME, 0.2, rust=0.3)
        for s0 in (-1, 0, 1, 2):
            for row, (z0, z1) in enumerate(SM_ROWS):
                if (f, s0, row) in reserved:
                    continue
                r = rng.random()
                kind = forced.get((f, s0, row)) or ("white" if r < 0.74 else "dark" if r < 0.88 else "patched")
                facility_panel(B, f, s0, z0, z1, kind)
    hazard(B, Vector((-0.99, 2.991, -0.995)), (1, 0, 0), (0, 0, 1), 3.98, 0.07, (0, 1, 0), pitch=0.1)
    hazard(B, Vector((2.99, 1.0, 0.955)), (0, 1, 0), (0, 0, 1), 1.98, 0.035, (1, 0, 0), pitch=0.08)

    # ---- front: output mouth at belt height in front of the anchor column (x = 0) ----
    RF = sm_basis("F")
    B.box(Vector((0, 2.955, -0.46)), (1.92, 0.05, 0.88), I3, "metal", C_FRAME, 0.2, rust=0.3)
    B.box(Vector((0, 2.975, -0.58)), (0.9, 0.02, 0.52), I3, "metal", (0.02, 0.02, 0.02), 0.05)        # dark mouth
    B.box(Vector((0, 2.982, -0.37)), (0.84, 0.01, 0.05), I3, "molten", C_MOLTEN, 0.05)                 # heat at the lintel
    B.box(Vector((0, 2.982, -0.8)), (0.7, 0.01, 0.04), I3, "molten", C_MOLTEN, 0.05)                   # glowing sill
    for sx in (-1, 1):
        B.box(Vector((sx * 0.5, 2.96, -0.55)), (0.08, 0.07, 0.72), I3, "steel", C_AMBER, 0.2, rust=0.2)
    B.box(Vector((0, 2.96, -0.2)), (1.08, 0.07, 0.08), I3, "steel", C_AMBER, 0.2, rust=0.2)
    B.box(Vector((0, 2.95, -0.86)), (0.92, 0.08, 0.03), Matrix.Rotation(math.radians(-15), 3, 'X'), "steel", L["C_WEAR"], 0.15, rust=0.3)
    B.box(Vector((-0.72, 2.982, -0.1)), (0.34, 0.01, 0.1), I3, "tape", C_TAPE, 0.25)                    # stencil tape
    # front right: inspection hatch with a porthole onto the melt
    B.box(Vector((2.0, 2.955, -0.46)), (1.92, 0.05, 0.88), I3, "metal", C_DARK, 0.2, rust=0.2)
    pc = Vector((2.0, 2.955, -0.46))
    B.cyl(pc + Vector((0, 0.025, 0)), pc + Vector((0, 0.03, 0)), 0.3, 24, "molten", C_MOLTEN, 0.05)
    ring(B, pc + Vector((0, 0.03, 0)), (0, 1, 0), 0.29, 0.39, 0.03, 24, "steel", C_STEEL, 0.2, rust=0.4)
    for k in range(8):
        a = k / 8 * math.tau
        p = pc + Vector((math.cos(a) * 0.34, 0.034, math.sin(a) * 0.34))
        B.cyl(p, p + Vector((0, 0.008, 0)), 0.014, 6, "steel", C_STEEL, rust=0.4)
    for z in (-0.72, -0.2):                                                                      # hinges
        B.cyl(Vector((1.52, 2.975, z - 0.06)), Vector((1.52, 2.975, z + 0.06)), 0.025, 8, "steel", C_STEEL, rust=0.5)
    B.pipe([Vector((2.42, 2.97, -0.58)), Vector((2.52, 2.982, -0.52)), Vector((2.52, 2.982, -0.4)), Vector((2.42, 2.97, -0.34))], 0.012, 6, "steel", C_STEEL)
    hazard(B, Vector((1.1, 2.981, -0.09)), (1, 0, 0), (0, 0, 1), 1.8, 0.06, (0, 1, 0), pitch=0.09)
    B.box(Vector((2.7, 2.982, -0.78)), (0.12, 0.01, 0.05), I3, "glow", C_AMBER, 0.05)

    # ---- back: salvaged air blower in the lower anchor-column cell (the fan is its own node) ----
    fc = Vector((0.5, -0.955, -0.46))
    B.box(fc + Vector((0, 0.005, 0)), (0.93, 0.03, 0.85), I3, "panel", C_WHITE, 0.3)
    ring(B, fc + Vector((0, -0.01, 0)), (0, 1, 0), 0.34, 0.41, 0.06, 24, "metal", C_DARK, 0.2, rust=0.3)
    for k in range(4):
        a = k / 4 * math.tau + math.pi / 4
        B.cyl(fc + Vector((math.cos(a) * 0.075, -0.02, math.sin(a) * 0.075)), fc + Vector((math.cos(a) * 0.33, -0.02, math.sin(a) * 0.33)),
              0.008, 5, "steel", C_STEEL, rust=0.3)                                               # guard spokes
    B.pipe([Vector((0.95, -0.975, -0.2)), Vector((1.3, -0.985, -0.1)), Vector((1.5, -0.985, 0.3)), Vector((1.6, -0.975, 0.9))], 0.015, 6)

    # ---- right side: gauge board on a white panel ----
    for k, y in enumerate((0.3, 0.7)):
        gp = Vector((2.985, y, 0.5))
        B.cyl(gp - Vector((0.008, 0, 0)), gp, 0.13, 16, "metal", C_DARK, 0.15)
        B.cyl(gp, gp + Vector((0.004, 0, 0)), 0.11, 16, "panel", (0.85, 0.84, 0.8), 0.1)
        a = math.radians(40 + 70 * k)
        B.box(gp + Vector((0.006, math.cos(a) * 0.045, math.sin(a) * 0.045)), (0.003, 0.09, 0.01), Matrix.Rotation(a, 3, 'X'), "glow", (1.0, 0.15, 0.1), 0.05)
    B.box(Vector((2.985, 0.5, 0.22)), (0.006, 0.6, 0.08), I3, "tape", C_TAPE, 0.25)

    # ---- upper level, back half: hopper walled on all four sides (fed from above) ----
    for sx in (-1, 1):
        x = 1 + sx * 1.9
        B.box(Vector((x, 0.0, 1.625)), (0.13, 1.98, 1.15), I3, "metal", C_FRAME, 0.2, rust=0.4)
        B.box(Vector((x - sx * 0.07, 0.0, 1.6)), (0.012, 1.7, 0.95), I3, "steel", L["C_WEAR"], 0.12, rust=0.1)   # wear liner
        for y0 in (-0.95, 0.03):                                                                  # facility cladding
            B.box(Vector((x + sx * 0.075, y0 + 0.46, 1.55)), (0.02, 0.9, 0.9), I3, "panel", C_WHITE if y0 < 0 or sx > 0 else C_DARK, 0.3)
        bolts(B, [Vector((x + sx * 0.085, y, z)) for y in (-0.93, -0.03, 0.05, 0.93) for z in (1.12, 1.98)], (sx, 0, 0))
        hazard(B, Vector((x + sx * 0.0865, -0.99 if sx > 0 else 0.99, 2.03)), (0, 1 if sx > 0 else -1, 0), (0, 0, 1), 1.98, 0.15, (sx, 0, 0), pitch=0.12)
    B.box(Vector((1.0, 0.925, 1.625)), (3.98, 0.13, 1.15), I3, "metal", C_FRAME, 0.2, rust=0.4)          # wall to the kiln
    B.box(Vector((1.0, 0.855, 1.6)), (3.7, 0.012, 0.95), I3, "steel", L["C_WEAR"], 0.12, rust=0.1)
    for x0 in (-0.95, 0.05, 2.03):                                                               # cladding facing the kiln
        B.box(Vector((x0 + 0.46, 1.0, 1.66)), (0.9, 0.02, 1.0), I3, "panel", C_WHITE if x0 != 0.05 else (0.6, 0.6, 0.58), 0.3)
    B.box(Vector((1.0, -0.925, 1.625)), (3.98, 0.13, 1.15), I3, "metal", C_FRAME, 0.2, rust=0.4)        # back wall
    B.box(Vector((1.0, -0.855, 1.6)), (3.7, 0.012, 0.95), I3, "steel", L["C_WEAR"], 0.12, rust=0.1)
    for x0 in (-0.95, 0.05, 1.05, 2.03):                                                         # cladding facing out
        B.box(Vector((x0 + 0.46, -1.0, 1.55)), (0.9, 0.02, 0.9), I3, "panel", C_WHITE if x0 != 1.05 else C_DARK, 0.3)
    hazard(B, Vector((-0.99, -1.0115, 2.03)), (1, 0, 0), (0, 0, 1), 3.98, 0.15, (0, -1, 0), pitch=0.12)
    for sx in (-1, 1):                                                                            # corner posts
        B.box(Vector((1 + sx * 1.925, -0.925, 1.625)), (0.14, 0.14, 1.15), I3, "metal", C_DARK, 0.2, rust=0.3)
    for (c, s) in ((Vector((1, 0.925, 2.22)), (3.98, 0.16, 0.06)), (Vector((1, -0.925, 2.22)), (3.98, 0.16, 0.06)),
                   (Vector((-0.91, 0, 2.22)), (0.16, 1.98, 0.06)), (Vector((2.91, 0, 2.22)), (0.16, 1.98, 0.06))):  # rolled rim
        B.box(c, s, I3, "metal", C_DARK, 0.2, rust=0.4)
    # hopper floor: grate bars over the glowing melt
    B.box(Vector((1.0, 0.0, 1.056)), (3.72, 1.72, 0.01), I3, "molten", C_MOLTEN, 0.05)
    for k in range(15):
        B.box(Vector((-0.75 + k * 0.25, 0.0, 1.085)), (0.05, 1.72, 0.05), I3, "metal", C_DARK, 0.15, rust=0.4)
    for y in (-0.5, 0.0, 0.5):
        B.box(Vector((1.0, y, 1.075)), (3.72, 0.04, 0.03), I3, "metal", C_DARK, 0.15, rust=0.4)

    # ---- upper level, front half: panelled kiln ----
    K = Vector((1.3, 2.0, 0.0))
    kr = lambda z, r: [K + Vector((math.cos(a) * r, math.sin(a) * r, z)) for a in [k / 24 * math.tau for k in range(24)]]
    B.tube_rings([kr(1.05, 0.92), kr(2.05, 0.92), kr(2.28, 0.78), kr(2.42, 0.5), kr(2.48, 0.3)], "metal", C_FRAME, 0.2, 0.3, cap=True, smooth=True)
    for k in range(12):
        a = (k + 0.5) / 12 * math.tau
        nrm = Vector((math.cos(a), math.sin(a), 0)); tan = Vector((-math.sin(a), math.cos(a), 0))
        R = Matrix((tan, Z, nrm)).transposed()
        for (z0, z1) in ((1.12, 1.6), (1.8, 2.0)):
            c = K + nrm * 0.935 + Z * ((z0 + z1) / 2)
            if k == 2 and z0 < 1.5:                                                               # peephole panel
                B.box(c, (0.45, z1 - z0, 0.03), R, "metal", C_DARK, 0.2)
                B.box(c + nrm * 0.017, (0.2, 0.12, 0.005), R, "molten", C_MOLTEN, 0.05)
                continue
            if k in (8, 9) and z0 < 1.5:                                                          # behind the feed duct
                continue
            r = rng.random()
            if r < 0.12:
                continue                                                                          # panel missing
            B.box(c, (0.45, z1 - z0, 0.03), R, "panel", C_WHITE if r < 0.85 else (0.6, 0.6, 0.58), 0.3)
    ring(B, K + Z * 1.7, (0, 0, 1), 0.9, 0.94, 0.12, 32, "molten", C_MOLTEN, 0.05)                 # molten seam
    ring(B, K + Z * 1.62, (0, 0, 1), 0.9, 0.955, 0.04, 32, "metal", C_DARK, 0.2, rust=0.3)
    ring(B, K + Z * 1.78, (0, 0, 1), 0.9, 0.955, 0.04, 32, "metal", C_DARK, 0.2, rust=0.3)
    ring(B, K + Z * 2.08, (0, 0, 1), 0.85, 0.95, 0.05, 32, "steel", C_STEEL, 0.2, rust=0.5)
    for k in range(12):                                                                          # dome ribs
        a = k / 12 * math.tau; d = Vector((math.cos(a), math.sin(a), 0))
        B.pipe([K + d * 0.93 + Z * 2.05, K + d * 0.8 + Z * 2.29, K + d * 0.52 + Z * 2.43, K + d * 0.3 + Z * 2.49], 0.02, 5, "steel", C_STEEL)
    B.cyl(K + Z * 2.46, K + Z * 2.53, 0.24, 16, "metal", C_DARK, 0.2, rust=0.3)                    # top vent
    B.box(K + Vector((0, 0, 2.54)), (0.1, 0.1, 0.01), I3, "glow", C_AMBER, 0.05)
    # feed duct from the hopper wall into the kiln
    B.box(Vector((1.3, 1.07, 1.4)), (0.56, 0.28, 0.44), I3, "metal", C_DARK, 0.2, rust=0.4)
    for y in (1.0, 1.17):
        B.box(Vector((1.3, y, 1.4)), (0.64, 0.03, 0.52), I3, "steel", C_STEEL, 0.2, rust=0.5)
    # chimney on scaffold at the front right
    st = Vector((2.55, 2.5, 0.0))
    B.cyl(st + Z * 1.05, st + Z * 2.92, 0.17, 14, "steel", (0.22, 0.22, 0.23), 0.2, rust=0.6)
    for z in (1.1, 1.9, 2.9):
        B.cyl(st + Z * (z - 0.03), st + Z * (z + 0.03), 0.21, 14, "metal", C_DARK, 0.15, rust=0.3)
    B.cyl(st + Z * 2.7, st + Z * 2.86, 0.176, 14, "metal", (0.04, 0.035, 0.03), 0.1)               # soot
    B.cyl(Vector((2.12, 2.28, 2.12)), st + Vector((-0.1, -0.05, 2.12)), 0.09, 10, "steel", (0.22, 0.22, 0.23), rust=0.6)
    for (x, y) in ((2.9, 2.15), (2.9, 2.85)):
        B.cyl(Vector((x, y, 1.05)), Vector((x, y, 2.6)), 0.024, 8, "steel", C_STEEL, rust=0.55)
        B.cyl(Vector((x, y, 2.3)), st + Vector((0.12 if y < 2.5 else 0.12, 0.0, 2.3)), 0.017, 6, "steel", C_STEEL, rust=0.5)
    B.cyl(Vector((2.9, 2.15, 1.6)), Vector((2.9, 2.85, 2.4)), 0.017, 6, "steel", C_STEEL, rust=0.6)   # brace

    # ---- upper level, front left: salvaged facility console ----
    cc = Vector((-0.5, 2.25, 1.55))
    B.box(cc, (0.7, 0.9, 1.0), I3, "panel", C_WHITE, 0.3)
    B.box(cc + Vector((0, 0, 0.52)), (0.74, 0.94, 0.04), I3, "metal", C_DARK, 0.2)
    B.box(cc + Vector((0, 0.455, 0.2)), (0.5, 0.01, 0.3), I3, "metal", (0.02, 0.03, 0.03), 0.05)          # screen
    for k in range(4):
        B.box(cc + Vector((-0.12 + k * 0.07, 0.462, 0.14 + (k % 2) * 0.06)), (0.05, 0.004, 0.012), I3, "glow", C_AMBER, 0.05)
    for k, col in enumerate((C_AMBER, C_CYAN, (1.0, 0.15, 0.1))):
        B.cyl(cc + Vector((-0.18 + k * 0.18, 0.45, -0.12)), cc + Vector((-0.18 + k * 0.18, 0.47, -0.12)), 0.035, 10, "glow", col, 0.05)
    B.box(cc + Vector((0, 0.455, -0.32)), (0.6, 0.012, 0.08), I3, "tape", C_TAPE, 0.25)
    hazard(B, cc + Vector((-0.35, 0.451, -0.49)), (1, 0, 0), (0, 0, 1), 0.7, 0.08, (0, 1, 0), pitch=0.08)
    for dx in (-0.06, 0.0, 0.06):                                                                 # cables to the kiln
        B.pipe([cc + Vector((0.35, dx, -0.3)), Vector((0.2, 2.25 + dx, 1.08)), Vector((0.45, 2.1 + dx, 1.08)), Vector((0.6, 2.0 + dx, 1.3))], 0.016, 6)
    B.cyl(cc + Vector((0.2, -0.3, 0.54)), cc + Vector((0.2, -0.3, 0.58)), 0.07, 12, "metal", C_DARK)
    B.cyl(cc + Vector((0.2, -0.3, 0.58)), cc + Vector((0.2, -0.3, 0.71)), 0.06, 12, "glow", C_AMBER, 0.05)

    # ---- overgrowth: moss creeping up from the floor and onto the deck ----
    for (p, r, sd) in ((Vector((-0.85, 2.82, -0.9)), 0.13, 1.0), (Vector((2.8, -0.82, -0.9)), 0.11, 2.0),
                       (Vector((-0.8, -0.8, -0.9)), 0.1, 3.0), (Vector((-0.82, 1.2, 1.06)), 0.1, 4.0)):
        rock(B, p, r, sd, 1, 0.3, "rock", lambda q, n: [c * random.uniform(0.8, 1.2) for c in C_MOSS], (1.2, 1.2, 0.35))
    finish(B, "Body", coll)

    # blower fan (spins about Y)
    F = Builder()
    for k in range(5):
        a = k / 5 * math.tau
        Rb = Matrix.Rotation(a, 3, 'Y') @ Matrix.Rotation(0.45, 3, 'X')
        F.box(Matrix.Rotation(a, 3, 'Y') @ Vector((0.19, 0, 0)), (0.28, 0.012, 0.1), Rb, "metal", (0.2, 0.2, 0.21), 0.15, rust=0.3)
    F.cyl(Vector((0, 0.02, 0)), Vector((0, -0.02, 0)), 0.06, 12, "metal", C_DARK, rust=0.2)
    finish(F, "Fan", coll, fc + Vector((0, -0.02, 0)))
    # rotating warning beacon on the console
    Rf = Builder()
    Rf.box(Vector((0, 0.02, 0)), (0.07, 0.01, 0.09), Matrix.Identity(3), "steel", (0.8, 0.8, 0.8), 0.05)
    finish(Rf, "Beacon", coll, cc + Vector((0.2, -0.3, 0.645)))
    return coll

# ======================================================================================
# Spawn tube: developer-placed item spawner styled as the test facility's tubes. 1 x 1 x 2 cells (origin = bottom
# cell centre, z -1..3). Items appear at the SpawnPoint marker high in the glass tube and drop out through the
# open hatch in its bottom, into whatever sits below (the museum mounts it over an item void).
# ======================================================================================
def build_spawn_tube():
    random.seed(91)
    coll = clear_collection("Prop_SpawnTube")
    B = Builder(); I3 = Matrix.Identity(3)
    # open drop hatch at the bottom: a yellow-framed square plate with a round hole, so spawned items fall
    # straight out of the tube into whatever is below (an item void in the museum)
    for (c, sz) in ((Vector((0, 0.86, -0.97)), (1.9, 0.18, 0.06)), (Vector((0, -0.86, -0.97)), (1.9, 0.18, 0.06)),
                    (Vector((0.86, 0, -0.97)), (0.18, 1.54, 0.06)), (Vector((-0.86, 0, -0.97)), (0.18, 1.54, 0.06))):
        B.box(c, sz, I3, "panel", C_YELLOW, 0.2)
    ring(B, Vector((0, 0, -0.96)), (0, 0, 1), 0.78, 0.95, 0.08, 40, "metal", C_DARK, 0.2, rust=0.3)
    hazard(B, Vector((-0.6, 0.951, -0.99)), (1, 0, 0), (0, 0, 1), 1.2, 0.05, (0, 1, 0), pitch=0.08)
    ring(B, Vector((0, 0, -0.9)), (0, 0, 1), 0.76, 0.92, 0.08, 40, "metal", C_DARK, 0.2, rust=0.2)
    # top collar, tie rods, lamp hood with its glow ring, feed pipes up through the ceiling
    ring(B, Vector((0, 0, 2.3)), (0, 0, 1), 0.76, 0.92, 0.08, 40, "metal", C_DARK, 0.2, rust=0.2)
    for k in range(6):
        a = k / 6 * math.tau + 0.26
        p = Vector((math.cos(a) * 0.86, math.sin(a) * 0.86, 0))
        B.cyl(p + Vector((0, 0, -0.86)), p + Vector((0, 0, 2.26)), 0.018, 6, "steel", C_STEEL, rust=0.3)
    hood = lambda z, r: [Vector((math.cos(a) * r, math.sin(a) * r, z)) for a in [k / 40 * math.tau for k in range(40)]]
    B.tube_rings([hood(2.36, 0.95), hood(2.62, 0.9), hood(2.85, 0.6), hood(2.92, 0.35)], "metal", C_DARK, 0.15, 0.1, cap=True, smooth=True)
    ring(B, Vector((0, 0, 2.37)), (0, 0, 1), 0.8, 0.93, 0.03, 40, "lamp", (1.0, 0.97, 0.9), 0.02)
    for dx in (-0.15, 0.15):
        B.cyl(Vector((dx, 0.1, 2.9)), Vector((dx, 0.1, 3.0)), 0.06, 10, "metal", (0.1, 0.1, 0.11), 0.1)
    B.box(Vector((0.4, -0.2, 2.9)), (0.3, 0.2, 0.1), I3, "panel", C_WHITE, 0.3)
    B.box(Vector((0.45, -0.2, 2.955)), (0.06, 0.06, 0.01), I3, "cyan", C_CYAN, 0.05)
    finish(B, "Body", coll)
    G = Builder()
    tube = lambda z: [Vector((math.cos(a) * 0.78, math.sin(a) * 0.78, z)) for a in [k / 40 * math.tau for k in range(40)]]
    G.tube_rings([tube(-0.86), tube(2.26)], "glass", (0.55, 0.9, 1.0), 0.02, 0.0, cap=False, smooth=True)
    finish(G, "Glass", coll)
    Rg = Builder()
    for z, r, tilt in ((-0.25, 0.6, 0.2), (0.0, 0.62, -0.25)):
        pts = [Vector((math.cos(a) * r, math.sin(a) * r, z + math.sin(a) * tilt * 0.3)) for a in [k / 32 * math.tau for k in range(32)]]
        Rg.pipe(pts + [pts[0]], 0.012, 5, "cyan", C_CYAN)
    finish(Rg, "Rings", coll, Vector((0, 0, 2.0)))
    sp = bpy.data.objects.new("SpawnPoint", None); coll.objects.link(sp); sp.location = (0, 0, 1.8)
    return coll

# ======================================================================================
# Ores
# ======================================================================================
def band(p, freq, off):
    return noise.noise(Vector((p.x * 0.6, p.y * 0.6, p.z * freq)) + off)

def build_iron(ground):
    random.seed(41 if not ground else 43)
    coll = clear_collection("Ore_IronGround" if ground else "Ore_Iron")
    B = Builder()
    def iron_col(p, n):
        b = band(p, 9.0, Vector((1.3, 0, 0)))
        if b > 0.2: c = (0.62, 0.2, 0.08)              # rust-red hematite band
        elif b > -0.15: c = (0.3, 0.17, 0.12)
        else: c = (0.16, 0.16, 0.18)                   # dark magnetite
        k = 0.8 + 0.4 * random.random()
        return [ci * k for ci in c]
    if not ground:
        rock(B, Vector((0, 0, 0)), 0.43, 1.0, 2, 0.3, "rock", iron_col, (1.05, 0.95, 0.8))
        # specular hematite plates bursting from one face, a rusty weep below them
        for k in range(7):
            a = random.uniform(-0.6, 0.6); e = random.uniform(0.1, 0.6)
            d = Vector((math.cos(a) * math.cos(e), math.sin(a) * math.cos(e), math.sin(e)))
            base = d * 0.3
            prism(B, base, base + (d + Vector((random.uniform(-.3, .3), random.uniform(-.3, .3), 0.2))).normalized() * random.uniform(0.12, 0.24),
                  random.uniform(0.035, 0.06), 6, 0.2, "metal", (0.62, 0.63, 0.68), 0.15)
    else:
        for k in range(9):
            d = Vector((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(-0.6, 0.8))).normalized()
            rock(B, d * random.uniform(0.06, 0.13), random.uniform(0.07, 0.11), k * 1.7, 1, 0.35, "metal",
                 lambda p, n: [c * random.uniform(0.8, 1.2) for c in random.choice([(0.3, 0.3, 0.33), (0.22, 0.22, 0.25), (0.4, 0.16, 0.08)])])
    finish(B, "Ore", coll)
    return coll

def build_copper(ground):
    random.seed(51 if not ground else 53)
    coll = clear_collection("Ore_CopperGround" if ground else "Ore_Copper")
    B = Builder()
    def matrix_col(p, n):
        b = band(p, 6.0, Vector((0, 4.2, 0)))
        if n.z > 0.3 and b > -0.1: c = (0.06, 0.42, 0.22)        # malachite crust on the upper faces
        elif b > 0.3: c = (0.1, 0.3, 0.2)
        else: c = (0.12, 0.13, 0.12)
        k = 0.8 + 0.4 * random.random()
        return [ci * k for ci in c]
    if not ground:
        rock(B, Vector((0, 0, -0.02)), 0.42, 2.0, 2, 0.32, "rock", matrix_col, (1.0, 1.05, 0.82))
        # azurite: a cluster of deep blue crystals
        for k in range(9):
            d = Vector((random.uniform(-0.4, 0.4), random.uniform(-0.4, 0.4), 1)).normalized()
            base = Vector((0.12, -0.05, 0.22)) + Vector((random.uniform(-0.1, 0.1), random.uniform(-0.1, 0.1), 0))
            prism(B, base, base + d * random.uniform(0.12, 0.28), random.uniform(0.025, 0.045), 6, 0.3, "gem",
                  (random.uniform(0.03, 0.08), random.uniform(0.1, 0.18), random.uniform(0.5, 0.75)), 0.1)
        # native copper nuggets pushing out of the matrix
        for k in range(6):
            d = Vector((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(-0.3, 0.6))).normalized()
            rock(B, d * 0.36, random.uniform(0.06, 0.1), 10 + k, 1, 0.4, "copper", lambda p, n: C_COPPER)
    else:
        for k in range(9):
            d = Vector((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(-0.6, 0.8))).normalized()
            pick = random.random()
            mat = "copper" if pick < 0.6 else "gem"
            col = C_COPPER if pick < 0.6 else (0.08, 0.5, 0.28)
            rock(B, d * random.uniform(0.06, 0.13), random.uniform(0.07, 0.11), k * 2.3, 1, 0.35, mat,
                 lambda p, n, col=col: [c * random.uniform(0.85, 1.15) for c in col])
    finish(B, "Ore", coll)
    return coll

# ======================================================================================
# Display plinth for the museum
# ======================================================================================
def build_plinth():
    random.seed(61)
    coll = clear_collection("Prop_Plinth")
    B = Builder(); I3 = Matrix.Identity(3)
    B.box(Vector((0, 0, -0.62)), (1.3, 1.3, 0.76), I3, "panel", C_WHITE, 0.3)
    B.box(Vector((0, 0, -0.97)), (1.42, 1.42, 0.06), I3, "metal", C_FRAME, 0.2, rust=0.3)
    B.box(Vector((0, 0, -0.225)), (1.36, 1.36, 0.05), I3, "metal", C_DARK, 0.2, rust=0.2)
    for n in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, -1, 0))):
        B.box(n * 0.68 + Vector((0, 0, -0.26)), (0.01 if n.x else 1.2, 0.01 if n.y else 1.2, 0.015), I3, "glow", C_AMBER, 0.05)
    finish(B, "Plinth", coll)
    return coll

# ======================================================================================
def export(coll, filename):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in coll.objects:
        ob.select_set(True)
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT_DIR, filename), export_format='GLB', use_selection=True,
                              export_yup=True, export_apply=True, export_vertex_color='ACTIVE',
                              export_all_vertex_colors=False, export_normals=True, export_texcoords=True,
                              export_materials='EXPORT', export_extras=False, export_cameras=False, export_lights=False)

PIECES = [
    (build_magnet, "magnet_tool.glb"), (build_crate, "item_crate.glb"), (build_grinder, "grinder.glb"),
    (build_spawner, "spawner.glb"), (build_void, "item_void.glb"), (lambda: build_iron(False), "iron_ore.glb"),
    (lambda: build_iron(True), "iron_ore_ground.glb"), (lambda: build_copper(False), "copper_ore.glb"),
    (lambda: build_copper(True), "copper_ore_ground.glb"), (build_plinth, "display_plinth.glb"),
    (build_smelter, "smelter.glb"), (build_spawn_tube, "spawn_tube.glb"),
]

def build_all(do_export=True):
    mats()
    built = []
    for fn, glb in PIECES:
        coll = fn()
        tag = coll.name
        for ob in coll.objects:
            base = ob.name.split(".")[0]
            ob.name = f"{tag}__{base}"
            if ob.data is not None:
                ob.data.name = ob.name
        built.append((coll, glb, tag))
    if do_export:
        for coll, glb, tag in built:
            for ob in coll.objects: ob.name = ob.name.split("__", 1)[1]
            export(coll, glb)
            for ob in coll.objects: ob.name = f"{tag}__{ob.name}"
    return built

if __name__ == "__main__":
    build_all(do_export=False)
