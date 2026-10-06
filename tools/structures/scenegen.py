"""
Helpers for writing structure scenes (.tscn) with Box3D colliders from plain Python (no Godot needed).

Frames: collider geometry is mostly described in the Blender frame the models were built in (Z up, +Y front)
and converted with b2g(): Godot = (x, z, -y). The scene root is the anchor cell's centre, as in the models.
Transform3D text is written row-major (Godot's Basis rows), matching the existing scenes.
"""
import os, re, json, struct, math, random, hashlib

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SENSOR_LAYER = 1073741824        # layer the machines' trigger sensors use (see Grinder.tscn)
TAG_METAL, TAG_RUBBER = 3, 5     # ItemTags used as impact-sound surfaces

SCRIPTS = {                      # res path -> uid (read from the .uid files where they exist)
    "structure": "res://game/scripts/items/placed/Structure.cs",
    "grinder": "res://game/scripts/items/placed/Grinder.cs",
    "spawner": "res://game/scripts/items/placed/ItemSpawner.cs",
    "spinner": "res://game/scripts/entities/Spinner.cs",
    "lever": "res://game/scripts/entities/Lever.cs",
    "oscillator": "res://game/scripts/entities/Oscillator.cs",
    "belt": "res://game/scripts/items/placed/ConveyorBelt.cs",
}

def res_to_abs(res):
    return os.path.join(REPO, res[len("res://"):])

def abs_to_res(path):
    return "res://" + os.path.relpath(path, REPO).replace(os.sep, "/")

# ---------------------------------------------------------------------------- numbers / vectors
def f(x):
    s = f"{x:.5f}".rstrip("0").rstrip(".")
    return "0" if s in ("-0", "") else s

def ff(x):
    """A float property value that always carries a decimal point ("40.0", not "40"). C# [Export] float/double
    properties set from an integer literal in a .tscn don't take (Godot drops them on the next save), and
    native float properties given an int get re-saved as overrides."""
    s = f(x)
    return s if any(c in s for c in ".e") else s + ".0"

def v3(v):
    return f"Vector3({f(v[0])}, {f(v[1])}, {f(v[2])})"

def add(a, b): return tuple(x + y for x, y in zip(a, b))
def sub(a, b): return tuple(x - y for x, y in zip(a, b))
def mul(a, k): return tuple(x * k for x in a)
def dot(a, b): return sum(x * y for x, y in zip(a, b))
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def length(a): return math.sqrt(dot(a, a))
def norm(a):
    l = length(a); return tuple(x / l for x in a)

def b2g(v):
    """Blender (x, y, z) -> Godot (x, z, -y)."""
    return (v[0], v[2], -v[1])

def mat_cols(c0, c1, c2):
    """3x3 as rows from three column vectors."""
    return [[c0[i], c1[i], c2[i]] for i in range(3)]

def mat_vec(m, v):
    return tuple(sum(m[i][j] * v[j] for j in range(3)) for i in range(3))

def mat_mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]

def rot_x(a):
    c, s = math.cos(a), math.sin(a); return [[1, 0, 0], [0, c, -s], [0, s, c]]
def rot_y(a):
    c, s = math.cos(a), math.sin(a); return [[c, 0, s], [0, 1, 0], [-s, 0, c]]
def rot_z(a):
    c, s = math.cos(a), math.sin(a); return [[c, -s, 0], [s, c, 0], [0, 0, 1]]
IDENT = [[1, 0, 0], [0, 1, 0], [0, 0, 1]]

def is_ident(m):
    return all(abs(m[i][j] - (1 if i == j else 0)) < 1e-9 for i in range(3) for j in range(3))

def xform_text(m, o):
    return "Transform3D(" + ", ".join(f(m[i][j]) for i in range(3) for j in range(3)) + ", " + ", ".join(f(x) for x in o) + ")"

# ---------------------------------------------------------------------------- uids / imports
# Godot's ResourceUID writes base 34 with the characters a-y and 0-8, never 'z' or '9' (an off-by-one kept for
# compatibility, see godot core/io/resource_uid.cpp and GH-83843). It reads 'z' and '0' as the same digit, and
# rewrites any uid holding a 'z' or '9' when it saves.
_ALPH = "abcdefghijklmnopqrstuvwxy012345678"

def uid_text(n):
    s = ""
    while n:
        s = _ALPH[n % 34] + s; n //= 34
    return "uid://" + s

def new_uid(seed_text):
    rnd = random.Random(hashlib.sha1(seed_text.encode()).hexdigest())
    return uid_text(rnd.getrandbits(62) | (1 << 61))

IMPORT_TEMPLATE = """[remap]

importer="scene"
importer_version=1
type="PackedScene"
uid="{uid}"
path="res://.godot/imported/{name}-{md5}.scn"

[deps]

source_file="{res}"
dest_files=["res://.godot/imported/{name}-{md5}.scn"]

[params]

nodes/root_type=""
nodes/root_name=""
nodes/root_script=null
mesh_library/use_node_names_as_mesh_names=false
array_mesh/deduplicate_surfaces=true
nodes/apply_root_scale=true
nodes/root_scale=1.0
nodes/import_as_skeleton_bones=false
nodes/use_name_suffixes=true
nodes/use_node_type_suffixes=true
meshes/ensure_tangents=true
meshes/generate_lods=true
meshes/create_shadow_meshes=true
meshes/light_baking=1
meshes/lightmap_texel_size=0.2
meshes/force_disable_compression=false
skins/use_named_skins=true
animation/import=true
animation/fps=30
animation/trimming=false
animation/remove_immutable_tracks=true
animation/import_rest_as_RESET=false
import_script/path="{script}"
materials/extract=0
materials/extract_format=0
materials/extract_path=""
_subresources={{}}
gltf/naming_version=2
gltf/embedded_image_handling=1
gltf/texture_map_mode=1
"""

def ensure_import(glb_res, import_script_res):
    """uid of a model; writes its .import (with the family's post-import script) if it has none."""
    path = res_to_abs(glb_res) + ".import"
    if os.path.exists(path):
        txt = open(path, encoding="utf-8").read()
        return txt.split('uid="', 1)[1].split('"', 1)[0]
    uid = new_uid(glb_res)
    open(path, "w", newline="\n").write(IMPORT_TEMPLATE.format(
        uid=uid, name=os.path.basename(glb_res), md5=hashlib.md5(glb_res.encode()).hexdigest(), res=glb_res, script=import_script_res))
    return uid

def script_uid(res):
    p = res_to_abs(res) + ".uid"
    return open(p).read().strip() if os.path.exists(p) else None

def glb_children(glb_res):
    """{node path under the model root: child index} for every node in the .glb (as Godot imports them)."""
    b = open(res_to_abs(glb_res), "rb").read()
    ln = struct.unpack("<I", b[12:16])[0]
    j = json.loads(b[20:20 + ln])
    out = {}
    def walk(indices, prefix):
        for k, i in enumerate(indices):
            n = j["nodes"][i]
            p = prefix + n["name"]
            out[p] = k
            walk(n.get("children", []), p + "/")
    walk(j["scenes"][j.get("scene", 0)]["nodes"], "")
    return out

# ---------------------------------------------------------------------------- scenes
class Scene:
    """One structure scene. Geometry calls take Godot coordinates; the *_b helpers take Blender ones.
    `xf` (a rotation about the root) is applied to every shape and to the model, for turned variants
    (the magnetic conveyors' wall and ceiling forms)."""

    def __init__(self, name, model_res=None, import_script=None, xf=None, xo=(0, 0, 0)):
        self.name = name
        self.ext_lines, self.ext_ids, self.nodes = [], {}, []
        self.belt_count = 0
        self.xf = xf or IDENT
        self.xo = xo                    # translation applied after xf (e.g. turning about the footprint centre)
        self.model_res = model_res
        self.model_nodes = glb_children(model_res) if model_res else {}
        if model_res:
            self.model_id = self.ext("PackedScene", model_res, ensure_import(model_res, import_script))

    def ext(self, kind, res, uid=None):
        if res in self.ext_ids:
            return self.ext_ids[res]
        i = f"{len(self.ext_ids) + 1}_r"
        uid = uid or (script_uid(res) if kind == "Script" else None)
        self.ext_lines.append(f'[ext_resource type="{kind}"' + (f' uid="{uid}"' if uid else "") + f' path="{res}" id="{i}"]')
        self.ext_ids[res] = i
        return i

    def script(self, key):
        return self.ext("Script", SCRIPTS[key])

    def node(self, header, *props):
        self.nodes += [header] + [p for p in props if p] + [""]

    # --- root and model
    def root(self, script_key, blueprint=None, cells=((0, 0, 0),), arrow=0, tags=(TAG_METAL,), extra=(), paths=None):
        header = f'[node name="{self.name}" type="Box3DBody"'
        if paths:
            header += ' node_paths=PackedStringArray(' + ", ".join(f'"{p}"' for p in paths) + ')'
        header += "]"
        props = ["body_type = 0", 'box_size = Vector3(0, 0, 0)', f'script = ExtResource("{self.script(script_key)}")']
        props += list(extra)
        if len(cells) > 1 or tuple(cells[0]) != (0, 0, 0):
            props.append("cellOffsets = Array[Vector3i]([" + ", ".join(f"Vector3i({a}, {b}, {c})" for a, b, c in cells) + "])")
        if blueprint is not None:
            props.append(f'blueprintItemID = "{blueprint}"')
        if arrow:
            props.append(f"flowArrow = {arrow}")
        if tags:
            props.append("tags = Array[int]([" + ", ".join(str(t) for t in tags) + "])")
        self.node(header, *props)

    def model(self):
        plain = is_ident(self.xf) and not any(self.xo)
        self.node(f'[node name="Model" parent="." instance=ExtResource("{self.model_id}")]',
                  None if plain else f"transform = {xform_text(self.xf, self.xo)}")

    def model_prop(self, path, *props):
        """Override properties of a node inside the model (e.g. the belt speed)."""
        parent, name = ("Model/" + path).rsplit("/", 1)
        idx = self.model_nodes.get(path)
        self.node(f'[node name="{name}" parent="{parent}"' + (f' index="{idx}"' if idx is not None else "") + "]", *props)

    def autoplay(self, clip="idle-loop"):
        """Start the model's baked animation clip (the salvage exports carry one looping clip)."""
        self.node('[node name="AnimationPlayer" parent="Model"]', f'autoplay = "{clip}"')

    def spin(self, path, axis_g, speed):
        """Cosmetic Spinner on a model node (axis in the node's local Godot frame, rad/s)."""
        self.model_prop(path, f'script = ExtResource("{self.script("spinner")}")', f"axis = {v3(axis_g)}", f"speed = {ff(speed)}")

    def oscillate(self, path, *props):
        """Cosmetic Oscillator on a model node (see game/scripts/entities/Oscillator.cs)."""
        self.model_prop(path, f'script = ExtResource("{self.script("oscillator")}")', *props)

    # --- shapes
    def box(self, name, size, pos, basis=None, parent=".", friction=None, material=TAG_METAL, tangent=None, extra=()):
        m = mat_mul(self.xf, basis or IDENT) if parent == "." else (basis or IDENT)
        p = add(mat_vec(self.xf, pos), self.xo) if parent == "." else pos
        t = (mat_vec(self.xf, tangent) if parent == "." else tangent) if tangent else None
        props = [f"box_size = {v3(size)}",
                 f"friction = {ff(friction)}" if friction is not None else None,
                 f"user_material_id = {material}" if material else None,
                 f"tangent_velocity = {v3(t)}" if t else None] + list(extra)
        props.append(f"position = {v3(p)}" if is_ident(m) else f"transform = {xform_text(m, p)}")
        self.node(f'[node name="{name}" type="Box3DCollisionShape" parent="{parent}"]', *props)

    def box_b(self, name, center, size, parent=".", **kw):
        """Axis-aligned box from Blender centre / size."""
        self.box(name, (size[0], size[2], size[1]), b2g(center), parent=parent, **kw)

    def obox_b(self, name, center, u, v, w, parent=".", **kw):
        """Oriented box from a Blender centre and three orthogonal full-edge vectors."""
        U, V, W = b2g(u), b2g(v), b2g(w)
        size = (length(U), length(V), length(W))
        cu, cv, cw = norm(U), norm(V), norm(W)
        if dot(cross(cu, cv), cw) < 0:
            cw = mul(cw, -1)
        self.box(name, size, b2g(center), mat_cols(cu, cv, cw), parent=parent, **kw)

    def body(self, name, pos_g=(0, 0, 0), basis=None, parent=".", sensor=False, size=None, script_key=None, extra=(), paths=None):
        """Child Box3DBody: a static sub-body (moving parts) or a trigger sensor (size = its box)."""
        m = mat_mul(self.xf, basis or IDENT) if parent == "." else (basis or IDENT)
        p = add(mat_vec(self.xf, pos_g), self.xo) if parent == "." else pos_g
        header = f'[node name="{name}" type="Box3DBody" parent="{parent}"'
        if paths:
            header += ' node_paths=PackedStringArray(' + ", ".join(f'"{x}"' for x in paths) + ')'
        header += "]"
        props = ["body_type = 0", f"box_size = {v3(size or (0, 0, 0))}"]
        if script_key:
            props.append(f'script = ExtResource("{self.script(script_key)}")')
        props += list(extra)
        if sensor:
            props += ["is_sensor = true", f"collision_layer = {SENSOR_LAYER}"]
        props.append(f"position = {v3(p)}" if is_ident(m) else f"transform = {xform_text(m, p)}")
        self.node(header, *props)

    def belt(self, points_g, speed, friction=None, climb=None, width=None, ret=False, ret_width=None, ret_depth=None, normal_g=(0, 1, 0), prefix="", visual="Belt"):
        """A ConveyorBelt: the carrying surface's centreline (Godot frame, in flow order) and how it moves. It has no
        collider of its own; the build grid merges belts whose ends meet into seamless lines (BuildGrid.Belts.cs).
        ret: the belt exposes its return run underneath (the conveyor families do; machines' belts don't).
        visual: name of the model's belt mesh, whose scrolling pattern the line keeps continuous across pieces."""
        pts = [add(mat_vec(self.xf, p), self.xo) for p in points_g]
        n = mat_vec(self.xf, normal_g)
        name = f"{prefix}Belt" + (str(self.belt_count) if self.belt_count else "")
        self.belt_count += 1
        props = [f'script = ExtResource("{self.script("belt")}")',
                 "points = PackedVector3Array(" + ", ".join(f"{f(a)}, {f(b)}, {f(c)}" for a, b, c in pts) + ")"]
        if any(abs(x - y) > 1e-9 for x, y in zip(n, (0, 1, 0))):
            props.append(f"normal = {v3(n)}")
        mesh_path = next((p for p in sorted(self.model_nodes) if visual and (p == visual or p.endswith("/" + visual))), None)
        if mesh_path:
            props.append(f'visual = NodePath("../Model/{mesh_path}")')
        props += [f"width = {ff(width if width is not None else 2 * BELT_W)}",
                  f"speed = {ff(speed)}",
                  f"climbSpeed = {ff(climb)}" if climb else None,
                  f"friction = {ff(friction if friction is not None else BELT_FRICTION)}"]
        if ret:
            props += [f"returnDepth = {ff(ret_depth if ret_depth is not None else BELT_H)}",
                      f"returnWidth = {ff(ret_width if ret_width is not None else 2 * LIP_X[0])}"]
        self.node(f'[node name="{name}" type="Node3D" parent="."]', *props)

    def sensor_b(self, name, center, size, parent="."):
        self.body(name, b2g(center) if parent == "." else center, sensor=True, size=(size[0], size[2], size[1]), parent=parent)

    def write(self, rel_path):
        path = os.path.join(REPO, rel_path)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        # keep a uid the editor has given the scene, so references to it by uid still resolve
        header = "[gd_scene format=3]"
        if os.path.exists(path):
            first = open(path, encoding="utf-8").readline()
            m = re.search(r'uid="([^"]+)"', first)
            if m:
                header = f'[gd_scene format=3 uid="{m.group(1)}"]'
        text = "\n".join([header, ""] + self.ext_lines + [""] + self.nodes).rstrip() + "\n"
        open(path, "w", newline="\n").write(text)
        return rel_path

# ---------------------------------------------------------------------------- belt profiles (Blender frame)
BELT_TOP = -0.85
BELT_H = 0.085
RET_Y = BELT_TOP - BELT_H
BELT_W = 0.84
TOP_T, RET_T = 0.04, 0.03
WALL_X = (0.84, 0.95)
LIP_X = (0.812, 0.842)
LIP_O = (-0.15, -0.095)
BELT_FRICTION, SLOPE_FRICTION, WALL_FRICTION = 0.8, 1.2, 0.1

def straight_profile(y0=-1.0):
    return lambda s: ((y0 + s, BELT_TOP), 0.0)

def bend_up_profile(total_y, rise, R, th):
    """Mirror of salvage_lib.bend_up_path: returns (profile(s) -> ((y, z), angle), length, flat, incline)."""
    ls = (rise - R * (1 - math.cos(th))) / math.sin(th)
    flat = total_y - R * math.sin(th) - ls * math.cos(th)
    def prof(s):
        if s <= flat:
            return (-1 + s, BELT_TOP), 0.0
        if s <= flat + R * th:
            ph = (s - flat) / R
            return (-1 + flat + R * math.sin(ph), BELT_TOP + R * (1 - math.cos(ph))), ph
        d = s - flat - R * th
        return (-1 + flat + R * math.sin(th) + d * math.cos(th), BELT_TOP + R * (1 - math.cos(th)) + d * math.sin(th)), th
    return prof, flat + R * th + ls, flat, ls

SLOPE_R, SLOPE_TH = 1.0, math.radians(30.0)
SLOPE_LS = (4.0 - 2 * SLOPE_R * math.sin(SLOPE_TH)) / math.cos(SLOPE_TH)

def slope_profile():
    """The basic slope (4 m run, 2 m rise, tangent-arc-tangent), as build_conveyors.slope_fn."""
    R, th, Ls = SLOPE_R, SLOPE_TH, SLOPE_LS
    a1 = R * th
    def prof(s):
        if s <= a1:
            ph = s / R; return (-1 + R * math.sin(ph), BELT_TOP + R * (1 - math.cos(ph))), ph
        if s <= a1 + Ls:
            d = s - a1
            return (-1 + R * math.sin(th) + d * math.cos(th), BELT_TOP + R * (1 - math.cos(th)) + d * math.sin(th)), th
        u = 2 * a1 + Ls - s; ph = u / R
        return (3 - R * math.sin(ph), BELT_TOP + 2 - R * (1 - math.cos(ph))), ph
    return prof, 2 * a1 + Ls

def seg_b(sc, name, prof, s0, s1, x, width, o0, o1, ref_top=True, parent=".", speed=None, y_bounds=None, **kw):
    """Box following a belt profile between s0 and s1, spanning offsets o0..o1 along the profile normal and
    width across x. Its reference face (top if ref_top) passes exactly through the offset profile points, so
    consecutive segments' working faces meet. speed: tangent velocity along the chord (negative = backward)."""
    (pa, pha), (pb, phb) = prof(s0), prof(s1)
    na, nb = (-math.sin(pha), math.cos(pha)), (-math.sin(phb), math.cos(phb))
    o = o1 if ref_top else o0
    a = (pa[0] + na[0] * o, pa[1] + na[1] * o)
    b = (pb[0] + nb[0] * o, pb[1] + nb[1] * o)
    d = (b[0] - a[0], b[1] - a[1]); ln = math.hypot(*d)
    t = (d[0] / ln, d[1] / ln); n = (-t[1], t[0])
    thick = o1 - o0
    sgn = -1 if ref_top else 1
    c = ((a[0] + b[0]) / 2 + n[0] * thick / 2 * sgn, (a[1] + b[1]) / 2 + n[1] * thick / 2 * sgn)
    if speed is not None:
        kw["tangent"] = b2g((0, t[0] * speed, t[1] * speed))
    if y_bounds is not None:
        # trim the ends so no corner passes the footprint's back / front faces (tilted walls at a slope's ends)
        lo, hi = y_bounds
        half_v = abs(n[0]) * thick / 2
        for end in (1, -1):
            tip = c[0] + end * t[0] * ln / 2 + half_v
            low = c[0] + end * t[0] * ln / 2 - half_v
            over = (tip - hi) if end * t[0] > 0 else (lo - low)
            if over > 1e-6 and abs(t[0]) > 1e-3:
                cut = over / abs(t[0])
                ln -= cut
                c = (c[0] - end * t[0] * cut / 2, c[1] - end * t[1] * cut / 2)
        d = (t[0] * ln, t[1] * ln)
    sc.obox_b(name, (x, c[0], c[1]), (width, 0, 0), (0, n[0] * thick, n[1] * thick), (0, d[0], d[1]), parent=parent, **kw)

def stations(runs):
    """Segment breakpoints from [(s0, s1, pieces)] runs."""
    out = set()
    for s0, s1, n in runs:
        for i in range(n + 1):
            out.add(round(s0 + (s1 - s0) * i / n, 6))
    return sorted(out)

BELT_SAG = 0.008   # how far a belt's simplified path may cut inside its curve (m)

def simplify(points, tol=BELT_SAG):
    """Douglas-Peucker: the fewest of `points` (always keeping both ends) that stay within tol of the rest. The
    belt colliders follow the model's curves only this closely: smoother for items, and plenty for the eye."""
    if len(points) < 3:
        return list(points)
    a, b = points[0], points[-1]
    ab = sub(b, a); lab = length(ab)
    def dist(p):
        ap = sub(p, a)
        return length(cross(ab, ap)) / lab if lab > 1e-12 else length(ap)
    i, d = max(((k, dist(points[k])) for k in range(1, len(points) - 1)), key=lambda t: t[1])
    if d <= tol:
        return [a, b]
    return simplify(points[:i + 1], tol)[:-1] + simplify(points[i:], tol)

def profile_points(prof, s0, s1, step=0.02):
    """Godot-frame points along the middle of a profile's belt surface from s0 to s1, finely sampled."""
    n = max(1, int(math.ceil((s1 - s0) / step)))
    return [b2g((0,) + tuple(prof(s0 + (s1 - s0) * i / n)[0])) for i in range(n + 1)]

def belt_along(sc, prof, st, speed, friction=BELT_FRICTION, prefix="", y_bounds=None, climb=None, reverse=False, ret=False):
    """A belt along a profile, from its first station to its last (reverse: the other way). The path is resampled
    and simplified (see simplify), so the stations only need to bound it. ret: expose the return run underneath."""
    pts = simplify(profile_points(prof, st[0], st[-1]))
    sc.belt(pts[::-1] if reverse else pts, speed, friction=friction, climb=climb, ret=ret, prefix=prefix)

def walls_along(sc, prof, st, wall_top, side, prefix="", lips=True, y_bounds=None):
    """One side's guard wall (stringer + panels, up to wall_top above the belt) and underside lip."""
    tag = "L" if side < 0 else "R"
    for i in range(len(st) - 1):
        seg_b(sc, f"{prefix}Wall{tag}{i}", prof, st[i], st[i + 1], side * sum(WALL_X) / 2, WALL_X[1] - WALL_X[0],
              -0.15, wall_top, friction=WALL_FRICTION, y_bounds=y_bounds)
        if lips:
            seg_b(sc, f"{prefix}Lip{tag}{i}", prof, st[i], st[i + 1], side * sum(LIP_X) / 2, LIP_X[1] - LIP_X[0],
                  LIP_O[0], LIP_O[1], friction=WALL_FRICTION, y_bounds=y_bounds)
