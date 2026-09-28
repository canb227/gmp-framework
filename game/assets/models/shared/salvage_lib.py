"""
Shared library for the salvage-style model build scripts.

Loads the conveyor helpers (build_conveyors.py) and the prop helpers (build_props.py) into one namespace and
adds what the newer families need: extra materials, pivot/marker nodes, node parenting, mirroring and export.

A family script pulls everything in with:

    LIB = os.path.join(<models dir>, "shared", "salvage_lib.py")
    _S = {"__name__": "salvage_lib", "__file__": LIB}
    exec(compile(open(LIB, encoding="utf-8").read(), LIB, "exec"), _S)
    globals().update({k: v for k, v in _S.items() if not k.startswith("__")})

Conventions (same as the conveyors and props): Blender Z up; +Y is the structure's front / flow direction
(Godot -Z); the origin is the centre of the anchor 2 m cell, whose floor is z = -1; albedo lives in the "Grime"
vertex colours. Multi-cell structures grow toward +X / +Y / +Z from the anchor unless noted.
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix, noise

MODELS = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))          # game/assets/models
_PROPS = os.path.join(MODELS, "props", "source", "build_props.py")
_G = {"__name__": "props_lib", "__file__": _PROPS}
exec(compile(open(_PROPS, encoding="utf-8").read(), _PROPS, "exec"), _G)
L = _G["L"]                                                                    # the conveyor library namespace
for _ns in (L, _G):                                                            # prop names win over conveyor ones
    for _k, _v in _ns.items():
        if not _k.startswith("__"):
            globals()[_k] = _v

I3 = Matrix.Identity(3)
ZV = Vector((0, 0, 1))
C_WEAR = L["C_WEAR"]
C_VIOLET = (0.62, 0.4, 1.0)                  # antigravity
C_LAMP = (1.0, 0.97, 0.9)                    # facility light strips
C_FACILITY = (0.78, 0.78, 0.76)              # clean facility panel (advanced tier)
C_BLUE = (0.16, 0.2, 0.26)                   # motor cans

def salvage_mats():
    """Conveyor + prop materials plus the extras the newer families use."""
    _G["mats"]()
    extra = dict(
        lamp=vc_mat("M_PropLamp", 0.3, 0.0, (1.0, 0.96, 0.88), 5.0),
        violet=vc_mat("M_PropVioletGlow", 0.3, 0.0, (0.6, 0.38, 1.0), 5.0),
        field_ag=vc_mat("M_FieldAntigrav", 0.2, 0.0, (0.55, 0.35, 1.0), 2.0),
        field_zp=vc_mat("M_FieldZeroPoint", 0.2, 0.0, (0.3, 0.9, 1.0), 2.0),
    )
    for k, m in extra.items():
        MATS[k] = m
        if k not in MAT_ORDER:
            MI[k] = len(MAT_ORDER); MAT_ORDER.append(k)

# ---------- nodes ----------
def node(B, name, coll, pivot=None, parent=None):
    """Mesh object from builder B (geometry in the node's local frame), placed at pivot, optionally parented
    (pivot is then relative to the parent's origin)."""
    ob = B.to_object(name, coll)
    if pivot is not None:
        ob.location = pivot
    if parent is not None:
        ob.parent = parent
    return ob

def marker(name, coll, loc, parent=None):
    """Empty exported as a plain Node3D: IO points and other anchors the scenes can find by name."""
    ob = bpy.data.objects.new(name, None)
    ob.empty_display_type = 'PLAIN_AXES'; ob.empty_display_size = 0.2
    coll.objects.link(ob)
    ob.location = loc
    if parent is not None:
        ob.parent = parent
    return ob

def facing_basis(n):
    """Basis whose local z is n (a face normal), local y is up where possible."""
    n = Vector(n).normalized()
    up = ZV if abs(n.z) < 0.9 else Vector((0, 1, 0))
    x = up.cross(n).normalized(); y = n.cross(x)
    return Matrix((x, y, n)).transposed()

def panel_face(B, c, n, w, h, col=None, seam=True, t=0.035, mat="panel"):
    """A facility wall panel centred on c facing n (w across, h up), with a dark seam line near its top."""
    R = facing_basis(n)
    B.box(c, (w, h, t), R, mat, col or C_WHITE, 0.3)
    if seam and h > 0.3:
        B.box(c + R @ Vector((0, h / 2 - 0.07, t / 2 + 0.002)), (w - 0.04, 0.012, 0.004), R, "metal", (0.25, 0.25, 0.25), 0.2)

def light_strip(B, p0, p1, r=0.015, mat="lamp", c=C_LAMP):
    """Thin glowing rod from p0 to p1."""
    B.cyl(p0, p1, r, 6, mat, c, 0.05)

def solid(B, pts, faces, mat, c, var=0.2, rust=0.0, smooth=False):
    """Closed solid from explicit points and faces (index lists); normals made outward."""
    vs = [B.bm.verts.new(Vector(p)) for p in pts]
    fs = []
    for f in faces:
        face = B.bm.faces.new([vs[i] for i in f]); face.material_index = MI[mat]; face.smooth = smooth
        fs.append(face)
    bmesh.ops.recalc_face_normals(B.bm, faces=fs)
    B.paint(fs, c, var, rust)
    return fs

def vprism(B, base, z0, z1, mat, c, var=0.2, rust=0.0):
    """Vertical prism over a convex polygon base [(x, y)] from z0 to z1."""
    n = len(base)
    pts = [(x, y, z0) for x, y in base] + [(x, y, z1) for x, y in base]
    faces = [list(range(n))[::-1], list(range(n, 2 * n))] + [[i, (i + 1) % n, n + (i + 1) % n, n + i] for i in range(n)]
    return solid(B, pts, faces, mat, c, var, rust)

def loft(B, rings, mat, c, var=0.2, rust=0.0, cap=True, smooth=False):
    """Skin through rings of equal point count (open or closed tube via tube_rings)."""
    return B.tube_rings([[Vector(p) for p in r] for r in rings], mat, c, var, rust, cap=cap, smooth=smooth)

def quad_ring(cx, cy, z, hx, hy):
    """Axis-aligned rectangle loop (for square lofts: funnels, tubes)."""
    return [(cx - hx, cy - hy, z), (cx + hx, cy - hy, z), (cx + hx, cy + hy, z), (cx - hx, cy + hy, z)]

# ---------- belt paths ----------
def bend_up_path(total_y, rise, R, th, stations=10):
    """Belt path along +Y from the cell's back face (y = -1) at belt height: flat, a concave bend of radius R up
    to angle th, then a straight incline, ending total_y further on and rise higher (a loader lip / launch ramp).
    Returns (Path, flat length, straight incline length)."""
    ls = (rise - R * (1 - math.cos(th))) / math.sin(th)
    flat = total_y - R * math.sin(th) - ls * math.cos(th)
    assert flat > 0 and ls > 0, (flat, ls)
    def fn(s):
        if s <= flat:
            ph, y, z = 0.0, -1 + s, BELT_TOP
        elif s <= flat + R * th:
            ph = (s - flat) / R
            y, z = -1 + flat + R * math.sin(ph), BELT_TOP + R * (1 - math.cos(ph))
        else:
            ph, d = th, s - flat - R * th
            y = -1 + flat + R * math.sin(th) + d * math.cos(th)
            z = BELT_TOP + R * (1 - math.cos(th)) + d * math.sin(th)
        return (Vector((0, y, z)), Vector((0, math.cos(ph), math.sin(ph))), Vector((1, 0, 0)), Vector((0, -math.sin(ph), math.cos(ph))))
    return Path(flat + R * th + ls, fn, stations), flat, ls

# loader: a straight cell whose last ~1.2 m bends up 20 deg to a lip 0.35 m above belt height, so items clear
# the 0.25 m guards of a conveyor running across its front (1.95 m so the tilted stringer ends stay in the cell)
LOADER = dict(total_y=1.95, rise=0.35, R=1.2, th=math.radians(20.0))

def sub_path(path, s0, s1):
    """The part of path between s0 and s1 as its own Path (for guards on part of a piece)."""
    return Path(s1 - s0, lambda s: path.fn(s0 + s), path.stations)

# ---------- mirroring ----------
def mirror_collection(src, dst_name, axis=0):
    """Copy of every object in collection src mirrored across the given axis (0 = X) through the origin,
    keeping node names, pivots and parenting. Winding is flipped so normals stay outward."""
    dst = clear_collection(dst_name)
    S = Matrix.Scale(-1, 4, Vector([1 if i == axis else 0 for i in range(3)]))
    made = {}
    for ob in src.objects:
        if ob.type == 'MESH':
            me = ob.data.copy()
            bm = bmesh.new(); bm.from_mesh(me)
            bmesh.ops.transform(bm, matrix=S, verts=bm.verts)
            bmesh.ops.reverse_faces(bm, faces=bm.faces)
            bm.to_mesh(me); bm.free()
            set_active_colors(me)
            nb = bpy.data.objects.new(ob.name.split(".")[0].split("__")[-1], me)
        else:
            nb = bpy.data.objects.new(ob.name.split(".")[0].split("__")[-1], None)
            nb.empty_display_type = 'PLAIN_AXES'; nb.empty_display_size = 0.2
        loc = ob.location.copy(); loc[axis] = -loc[axis]
        nb.location = loc
        dst.objects.link(nb)
        made[ob.name] = nb
    for ob in src.objects:
        if ob.parent is not None and ob.parent.name in made:
            made[ob.name].parent = made[ob.parent.name]
    return dst

# ---------- export ----------
def export_glb(coll, path):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in coll.objects:
        ob.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True,
                              export_yup=True, export_apply=True, export_vertex_color='ACTIVE',
                              export_all_vertex_colors=False, export_normals=True, export_texcoords=True,
                              export_materials='EXPORT', export_extras=False, export_cameras=False, export_lights=False)

def build_family(pieces, out_dir, do_export=True):
    """pieces: [(builder_fn, glb filename)]. A builder returns its collection (named after the piece). Objects are
    named "<Collection>__<Node>" in the .blend so names stay unique, and exported with plain node names."""
    salvage_mats()
    built = []
    for fn, glb in pieces:
        coll = fn()
        tag = coll.name
        for ob in coll.objects:
            base = ob.name.split(".")[0].split("__")[-1]
            ob.name = f"{tag}__{base}"
            if ob.data is not None:
                ob.data.name = ob.name
        built.append((coll, glb, tag))
    if do_export:
        for coll, glb, tag in built:
            for ob in coll.objects: ob.name = ob.name.split("__", 1)[1]
            export_glb(coll, os.path.join(out_dir, glb))
            for ob in coll.objects: ob.name = f"{tag}__{ob.name}"
    return built
