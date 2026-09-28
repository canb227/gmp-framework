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
        field_heat=vc_mat("M_FieldHeat", 0.2, 0.0, (1.0, 0.45, 0.1), 2.0),
        field_cold=vc_mat("M_FieldCold", 0.2, 0.0, (0.6, 0.85, 1.0), 2.0),
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

# ---------- animation ----------
ANIM_FPS = 30
IDLE = "idle-loop"          # Godot imports clips whose names end in "-loop" as looping

def keys(ob, frames, prop, values, linear=False):
    """Keyframe ob.<prop> (location / rotation_euler / scale) to values at frames, on ob's idle action. The
    collection's clip length is the latest frame keyed on any of its objects (see anim_length)."""
    if ob.animation_data is None or ob.animation_data.action is None:
        ob.animation_data_create()
        ob.animation_data.action = bpy.data.actions.new(ob.name + "_idle")
    for fr, val in zip(frames, values):
        setattr(ob, prop, val)
        ob.keyframe_insert(prop, frame=fr)
    for fc in anim_fcurves(ob.animation_data.action):
        if linear:
            for kp in fc.keyframe_points:
                kp.interpolation = 'LINEAR'
        if not any(m.type == 'CYCLES' for m in fc.modifiers):
            fc.modifiers.new('CYCLES')           # short motions repeat through a longer clip when it is sampled
    setattr(ob, prop, values[0])

def anim_fcurves(act):
    if hasattr(act, "fcurves") and len(act.fcurves):
        return list(act.fcurves)
    out = []                                   # Blender 4.4+ layered actions
    for layer in getattr(act, "layers", []):
        for strip in layer.strips:
            for bag in strip.channelbags:
                out += list(bag.fcurves)
    return out

def spin(ob, axis, turns, length, rest=None):
    """Steady rotation about a local axis (0 x, 1 y, 2 z): `turns` full turns over `length` frames."""
    r0 = list(rest if rest is not None else ob.rotation_euler)
    r1 = list(r0); r1[axis] += math.tau * turns
    keys(ob, (1, length + 1), "rotation_euler", (tuple(r0), tuple(r1)), linear=True)

def cycle(ob, prop, poses, length, linear=False):
    """Loop through poses evenly over `length` frames, ending back on the first."""
    n = len(poses)
    frames = [1 + round(length * i / n) for i in range(n)] + [length + 1]
    keys(ob, frames, prop, list(poses) + [poses[0]], linear)

def anim_length(coll):
    end = 0
    for ob in coll.objects:
        if ob.animation_data and ob.animation_data.action:
            for fc in anim_fcurves(ob.animation_data.action):
                end = max(end, int(fc.range()[1]))
    return end

def rename_glb_animation(path, name):
    """The exporter calls a merged clip "Animation"; give it `name` by rewriting the .glb's JSON chunk."""
    import json, struct
    b = open(path, "rb").read()
    ln = struct.unpack("<I", b[12:16])[0]
    j = json.loads(b[20:20 + ln])
    for a in j.get("animations", []):
        a["name"] = name
    js = json.dumps(j, separators=(",", ":")).encode()
    js += b" " * ((4 - len(js) % 4) % 4)
    rest = b[20 + ln:]
    out = b[:8] + struct.pack("<I", 12 + 8 + len(js) + len(rest)) + struct.pack("<I", len(js)) + b"JSON" + js + rest
    open(path, "wb").write(out)

def unroll_cycles(coll, length):
    """The glTF exporter samples each curve over its own key range, so repeat shorter cycles as real keys up to
    the clip length (every model then loops as one clip with all its parts in step)."""
    for ob in coll.objects:
        if not (ob.animation_data and ob.animation_data.action):
            continue
        for fc in anim_fcurves(ob.animation_data.action):
            pts = [(kp.co[0], kp.co[1], kp.interpolation) for kp in fc.keyframe_points]
            if len(pts) < 2:
                continue
            first, last = pts[0][0], pts[-1][0]
            period = last - first
            if period <= 0 or last >= length + 1:
                continue
            delta = pts[-1][1] - pts[0][1]
            turns = delta / math.tau
            # whole-turn spins keep climbing; everything else repeats exactly (a pulse jumps back to its start)
            base = delta if fc.data_path == "rotation_euler" and abs(turns) > 0.5 and abs(turns - round(turns)) < 1e-3 else 0.0
            k = 1
            while first + k * period < length + 1 - 1e-6:
                for fr, val, interp in pts[1:]:
                    kp = fc.keyframe_points.insert(fr + k * period, val + base * k, options={'FAST'})
                    kp.interpolation = interp
                k += 1
            fc.update()

# ---------- export ----------
def export_glb(coll, path):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in coll.objects:
        ob.select_set(True)
    length = anim_length(coll)
    sc = bpy.context.scene
    anim = {}
    if length:
        unroll_cycles(coll, length)
        sc.render.fps = ANIM_FPS
        sc.frame_start, sc.frame_end = 1, length
        anim = dict(export_animations=True, export_animation_mode='ACTIVE_ACTIONS', export_force_sampling=True,
                    export_frame_range=True)
    else:
        anim = dict(export_animations=False)
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True,
                              export_yup=True, export_apply=True, export_vertex_color='ACTIVE',
                              export_all_vertex_colors=False, export_normals=True, export_texcoords=True,
                              export_materials='EXPORT', export_extras=False, export_cameras=False, export_lights=False, **anim)
    if length:
        rename_glb_animation(path, IDLE)

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
