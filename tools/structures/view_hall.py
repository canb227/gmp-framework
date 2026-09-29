"""
Renders the Object Museum's wing scenes (game/scenes/levels/museum/*.tscn: structures, walls, decor and signs) for review
without Godot. VIEW_SECTION=<Wing>[,<Wing>] limits it to some wings (default: all).
Needs Blender's Python module (pip install bpy==4.5.14).

    python3 tools/structures/view_hall.py <out.png> [top | lab | wing | cam_x,cam_y,cam_z:target_x,target_y,target_z] (Godot coordinates)
"""
import os, sys, re, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scenegen import REPO, res_to_abs
import bpy
from mathutils import Matrix, Vector

G2B = Matrix(((1, 0, 0), (0, 0, -1), (0, 1, 0)))
COLORS = {"hall_box_panel": (0.75, 0.75, 0.72), "hall_box_frame": (0.06, 0.06, 0.07), "hall_box_hazard": (0.85, 0.6, 0.05),
          "hall_box_floor": (0.2, 0.2, 0.21), "hall_box_tar": (0.02, 0.018, 0.015)}

def xf(text):
    v = [float(x) for x in text.split("(", 1)[1].rstrip(")").split(",")]
    return Matrix((v[0:3], v[3:6], v[6:9])), Vector(v[9:12])

def to_blender(M, o):
    return Matrix.Translation(G2B @ o) @ (G2B @ M @ G2B.transposed()).to_4x4()

def mat(name, rgb, emit=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]; b.inputs["Base Color"].default_value = (*rgb, 1)
    if emit:
        b.inputs["Emission Color"].default_value = (*rgb, 1); b.inputs["Emission Strength"].default_value = emit
    return m

SECTIONS = ("StructureHall", "ConceptLab", "MaterialsWing", "PuzzleRooms", "LowerLevel")
_glbs = {}
_cube = None

def place_glb(sc, glb, T):
    """Instance a model: each .glb is imported once into a hidden collection, then copied (sharing mesh data)."""
    if glb not in _glbs:
        before = set(sc.objects)
        bpy.ops.import_scene.gltf(filepath=res_to_abs(glb))
        obs = [x for x in sc.objects if x not in before]
        hold = bpy.data.collections.new("glb")
        for ob in obs:
            for c in list(ob.users_collection):
                c.objects.unlink(ob)
            hold.objects.link(ob)
            ob.animation_data_clear()                     # keyframes would pull the parts back to the model origin
        _glbs[glb] = obs
    copies = {}
    for ob in _glbs[glb]:
        c = ob.copy(); copies[ob] = c; sc.collection.objects.link(c)
        if ob.name.startswith(("Field", "Heat", "Steam", "Core")):     # additive shells in Godot; opaque here
            c.hide_render = True
    for ob, c in copies.items():
        if ob.parent is not None:
            c.parent = copies[ob.parent]
        else:
            c.matrix_world = T @ ob.matrix_world

def box(sc, mid, M):
    global _cube
    if _cube is None:
        bpy.ops.mesh.primitive_cube_add(size=1); tmp = bpy.context.object
        _cube = tmp.data; bpy.data.objects.remove(tmp)
    me = bpy.data.meshes.get("Box_" + mid)
    if me is None:
        me = _cube.copy(); me.name = "Box_" + mid; me.materials.append(mat(mid, COLORS.get(mid, (0.5, 0.5, 0.5))))
    ob = bpy.data.objects.new("Box", me); sc.collection.objects.link(ob); ob.matrix_world = M

def load_scene(sc, path, base):
    """Add one wing scene (or any scene built from boxes, models and labels) under the Godot transform base."""
    txt = open(path, encoding="utf-8").read()
    ext = dict((m.group(2), m.group(1)) for m in re.finditer(r'path="(res://[^"]+)" id="([^"]+)"', txt))
    mms = {m.group(1): (m.group(2), [float(x) for x in m.group(3).split(",")])
           for m in re.finditer(r'\[sub_resource type="MultiMesh" id="([^"]+)"\][^\[]*?mesh = SubResource\("([^"]+)"\)\nbuffer = PackedFloat32Array\(([^)]*)\)', txt)}
    frames = {}
    for blk in re.split(r"\n(?=\[node )", txt[txt.find("[node "):]):
        head = blk.split("\n", 1)[0]
        props = dict(l.split(" = ", 1) for l in blk.split("\n")[1:] if " = " in l)
        name = re.search(r'name="([^"]+)"', head).group(1)
        par = re.search(r'parent="([^"]+)"', head)
        par = par.group(1) if par else None
        key = "." if par is None else (name if par == "." else par + "/" + name)
        pm, po = frames.get(par, base) if par else base
        M, o = (Matrix.Identity(3), Vector())
        if "transform" in props:
            M, o = xf(props["transform"])
        elif "position" in props:
            o = Vector([float(x) for x in props["position"].split("(", 1)[1].rstrip(")").split(",")])
        if par is not None and name == "Room" and os.environ.get("ROOM_ANGLE"):          # the rotating room turned (degrees about X)
            M = M @ Matrix.Rotation(math.radians(float(os.environ["ROOM_ANGLE"])), 3, 'X')
        Mw, ow = (pm @ M, pm @ o + po) if par is not None else base
        frames[key] = (Mw, ow)
        inst = re.search(r'instance=ExtResource\("([^"]+)"\)', head)
        if inst:
            if ext[inst.group(1)].endswith(".glb"):
                scene, glb = "", ext[inst.group(1)]
            else:
                scene = open(res_to_abs(ext[inst.group(1)])).read()
                m = re.search(r'path="(res://[^"]+\.glb)"', scene)
                if not m:
                    print("no model in", ext[inst.group(1)]); continue
                glb = m.group(1)
            mt = re.search(r'\[node name="Model"[^\n]*\]\ntransform = (Transform3D\([^)]+\))', scene)
            MM, mo = xf(mt.group(1)) if mt else (Matrix.Identity(3), Vector())
            place_glb(sc, glb, to_blender(Mw @ MM, Mw @ mo + ow))
        elif 'type="MultiMeshInstance3D"' in head:
            mid, buf = mms[re.search(r'SubResource\("([^"]+)"\)', props["multimesh"]).group(1)]
            if mid == "hall_box_glass":
                continue
            for k in range(0, len(buf), 12):
                v = buf[k:k + 12]
                Mi = Matrix((v[0:3], v[4:7], v[8:11])); oi = Vector((v[3], v[7], v[11]))
                box(sc, mid, to_blender(Mw @ Mi, Mw @ oi + ow))
        elif 'type="MeshInstance3D"' in head and "hall_box_" in props.get("mesh", ""):
            mid = re.search(r'SubResource\("([^"]+)"\)', props["mesh"]).group(1)
            if mid != "hall_box_glass":
                box(sc, mid, to_blender(Mw, ow))
        elif 'type="Label3D"' in head and "text" in props:
            cu = bpy.data.curves.new("t", 'FONT'); cu.body = props["text"].strip('"').replace("\\n", "\n")
            size = int(props.get("font_size", "32")) * float(props.get("pixel_size", "0.01"))
            cu.size = size; cu.align_x = 'CENTER'
            ob = bpy.data.objects.new("Label", cu); sc.collection.objects.link(ob)
            ob.data.materials.append(mat("LabelMat", (1, 1, 1), 2.0))
            ob.matrix_world = to_blender(Mw, ow) @ Matrix.Rotation(math.pi / 2, 4, 'X')

def main(out, view):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    names = os.environ.get("VIEW_SECTION", ",".join(SECTIONS)).split(",")      # e.g. VIEW_SECTION=LowerLevel
    for name in names:
        load_scene(sc, os.path.join(REPO, "game", "scenes", "levels", "museum", name + ".tscn"), (Matrix.Identity(3), Vector()))
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera = cam
    if view == "wing":
        cam.data.type = 'ORTHO'; cam.data.ortho_scale = 84
        cam.location = G2B @ Vector((0, 120, 88)); cam.rotation_euler = (0, 0, 0)
        sc.render.resolution_x, sc.render.resolution_y = 1200, 900
    elif view == "lab":
        cam.data.type = 'ORTHO'; cam.data.ortho_scale = 84
        cam.location = G2B @ Vector((0, 120, -90)); cam.rotation_euler = (0, 0, 0)
        sc.render.resolution_x, sc.render.resolution_y = 1200, 900
    elif view == "top":
        cam.data.type = 'ORTHO'; cam.data.ortho_scale = 124
        cam.location = G2B @ Vector((88, 120, 0)); cam.rotation_euler = (0, 0, 0)
        sc.render.resolution_x, sc.render.resolution_y = 1000, 1260
    else:
        a, b = view.split(":")
        pos = G2B @ Vector([float(x) for x in a.split(",")]); tgt = G2B @ Vector([float(x) for x in b.split(",")])
        cam.location = pos; cam.rotation_euler = (tgt - pos).to_track_quat('-Z', 'Y').to_euler(); cam.data.lens = 22
        sc.render.resolution_x, sc.render.resolution_y = 1400, 800
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN')); sc.collection.objects.link(sun)
    sun.data.energy = 3.0; sun.rotation_euler = (math.radians(40), math.radians(10), math.radians(-35))
    w = bpy.data.worlds.new("W"); w.use_nodes = True; w.node_tree.nodes["Background"].inputs[0].default_value = (0.5, 0.52, 0.56, 1)
    sc.world = w
    sc.render.engine = "CYCLES"; sc.cycles.device = "CPU"; sc.cycles.samples = 12; sc.cycles.use_denoising = True
    sc.render.filepath = out
    bpy.ops.render.render(write_still=True)

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "top")
