"""
Renders a structure scene's Box3D colliders over its model, for checking scenes without Godot.
Solid shapes are red wireframes, sensors blue. Needs Blender's Python module (pip install bpy==4.5.14).

    python3 tools/structures/view_colliders.py <out.png> <scene.tscn> [view_x,view_y,view_z]
"""
import os, sys, math, re
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scenegen import REPO, IDENT, mat_mul, mat_vec, add, res_to_abs
from gen_scenes import parse, parse_xf, nums
import bpy
from mathutils import Matrix, Vector

G2B = Matrix(((1, 0, 0), (0, 0, -1), (0, 1, 0)))          # Godot vector -> Blender vector

def shapes(path):
    nodes = parse(path)
    frames = {".": (IDENT, (0.0, 0.0, 0.0))}
    sensors = set()
    out, model = [], None
    for n in nodes[1:]:
        if n["type"] == "Box3DBody" and n["parent"] is not None:
            m, o = parse_xf(n["props"])
            pm, po = frames.get(n["parent"], (IDENT, (0, 0, 0)))
            key = n["name"] if n["parent"] == "." else n["parent"] + "/" + n["name"]
            frames[key] = (mat_mul(pm, m), add(mat_vec(pm, o), po))
            if n["props"].get("is_sensor") == "true":
                sensors.add(key)
                if "box_size" in n["props"] and any(nums(n["props"]["box_size"])):
                    out.append((key, nums(n["props"]["box_size"]), frames[key][0], frames[key][1], True))
        if n["name"] == "Model" and n["parent"] == ".":
            model = parse_xf(n["props"])
    for n in nodes[1:]:
        if n["type"] != "Box3DCollisionShape":
            continue
        m, o = parse_xf(n["props"])
        pm, po = frames.get(n["parent"], (IDENT, (0, 0, 0)))
        out.append((n["name"], nums(n["props"]["box_size"]), mat_mul(pm, m), add(mat_vec(pm, o), po), n["parent"] in sensors))
    glb = re.search(r'path="(res://[^"]+\.glb)"', open(path, encoding="utf-8").read())
    return out, (glb.group(1) if glb else None), model

def mat(name, rgb):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*rgb, 1); b.inputs["Emission Color"].default_value = (*rgb, 1)
    b.inputs["Emission Strength"].default_value = 3.0
    return m

def main(out_png, scene_path, view=(1.2, 1.3, 0.8)):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    boxes, glb, model = shapes(scene_path)
    if glb:
        bpy.ops.import_scene.gltf(filepath=res_to_abs(glb))
        if model and model != (IDENT, (0.0, 0.0, 0.0)):
            m, o = model
            M = G2B @ Matrix(m) @ G2B.transposed()
            T = Matrix.Translation(G2B @ Vector(o)) @ M.to_4x4()
            for ob in list(sc.objects):
                if ob.parent is None:
                    ob.matrix_world = T @ ob.matrix_world
    red, blue = mat("Solid", (1.0, 0.1, 0.05)), mat("Sensor", (0.1, 0.4, 1.0))
    for name, size, m, o, sensor in boxes:
        bpy.ops.mesh.primitive_cube_add(size=1)
        ob = bpy.context.object
        ob.name = "COL_" + name
        M = G2B @ Matrix(m) @ G2B.transposed()
        Sz = Matrix.Diagonal(G2B @ Vector(size)).to_4x4()
        S = Matrix.Diagonal([abs(x) for x in (G2B @ Vector(size))]).to_4x4()
        ob.matrix_world = Matrix.Translation(G2B @ Vector(o)) @ M.to_4x4() @ S
        wf = ob.modifiers.new("wf", 'WIREFRAME'); wf.thickness = 0.02
        ob.data.materials.append(blue if sensor else red)
    pts = [ob.matrix_world @ Vector(c) for ob in sc.objects if ob.type == 'MESH' for c in ob.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    ctr = (lo + hi) / 2
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera = cam
    d = Vector(view).normalized()
    cam.location = ctr + d * ((hi - lo).length * 1.7)
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN')); sc.collection.objects.link(sun)
    sun.data.energy = 3.0; sun.rotation_euler = (math.radians(45), math.radians(15), math.radians(-40))
    w = bpy.data.worlds.new("W"); w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.57, 0.62, 1)
    sc.world = w
    sc.render.engine = "CYCLES"; sc.cycles.device = "CPU"; sc.cycles.samples = 12; sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = 700, 520
    sc.render.filepath = out_png
    bpy.ops.render.render(write_still=True)

if __name__ == "__main__":
    a = sys.argv[1:]
    main(a[0], os.path.join(REPO, a[1]) if not os.path.isabs(a[1]) else a[1],
         tuple(float(x) for x in a[2].split(",")) if len(a) > 2 else (1.2, 1.3, 0.8))
