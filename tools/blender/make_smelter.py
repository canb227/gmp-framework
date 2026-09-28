"""Procedurally build a test "smelter" prop and export it for Godot.

Run headless:
    blender -b -P tools/blender/make_smelter.py -- <out_dir> [--render]

Writes <out_dir>/smelter.glb and <out_dir>/smelter.blend, plus
<out_dir>/smelter_preview.png when --render is given.
"""
import math
import os
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_DIR = os.path.abspath(argv[0] if argv else ".")
RENDER = "--render" in argv
os.makedirs(OUT_DIR, exist_ok=True)

# Start from an empty scene.
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


def material(name, color, metallic=0.0, roughness=0.5, emission=None, strength=0.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = strength
    return mat


def finish(obj, name, mat, bevel=0.0, smooth=False):
    obj.name = name
    obj.data.materials.append(mat)
    if bevel:
        mod = obj.modifiers.new("Bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 2
    if smooth:
        bpy.ops.object.shade_smooth()
    return obj


steel = material("Steel", (0.35, 0.37, 0.40), metallic=0.9, roughness=0.35)
brick = material("FurnaceBrick", (0.45, 0.18, 0.10), roughness=0.85)
dark = material("DarkMetal", (0.08, 0.08, 0.09), metallic=0.8, roughness=0.5)
hazard = material("HazardYellow", (0.95, 0.70, 0.05), roughness=0.6)
glow = material("MoltenGlow", (1.0, 0.35, 0.05), emission=(1.0, 0.25, 0.02), strength=2.5)

# Base plate (1.6 x 1.6 m footprint, matches a small factory tile).
bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0.1))
base = bpy.context.object
base.scale = (1.6, 1.6, 0.2)
finish(base, "Base", steel, bevel=0.03)

# Hazard trim strips along the base's edges.
for i, (x, y, sx, sy) in enumerate(((0, 0.74, 1.6, 0.12), (0, -0.74, 1.6, 0.12),
                                    (0.74, 0, 0.12, 1.36), (-0.74, 0, 0.12, 1.36))):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(x, y, 0.21))
    strip = bpy.context.object
    strip.scale = (sx, sy, 0.02)
    finish(strip, f"HazardTrim{i}", hazard)

# Furnace body.
bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=0.6, depth=1.2, location=(0, 0, 0.8))
body = finish(bpy.context.object, "FurnaceBody", brick, bevel=0.04, smooth=True)

# Steel bands around the body.
for i, z in enumerate((0.35, 0.8, 1.25)):
    bpy.ops.mesh.primitive_torus_add(major_radius=0.61, minor_radius=0.035, location=(0, 0, z))
    finish(bpy.context.object, f"Band{i}", steel, smooth=True)

# Domed cap.
bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, radius=0.6, location=(0, 0, 1.4))
cap = bpy.context.object
cap.scale = (1, 1, 0.45)
finish(cap, "Cap", steel, smooth=True)

# Chimney.
bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=0.12, depth=0.9, location=(0.25, 0.2, 1.9))
finish(bpy.context.object, "Chimney", dark, bevel=0.01, smooth=True)
bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=0.16, depth=0.08, location=(0.25, 0.2, 2.35))
finish(bpy.context.object, "ChimneyLip", steel, smooth=True)

# Glowing furnace mouth on the front (-Y).
bpy.ops.mesh.primitive_cube_add(size=1, location=(0, -0.56, 0.6))
mouth_frame = bpy.context.object
mouth_frame.scale = (0.5, 0.12, 0.4)
finish(mouth_frame, "MouthFrame", dark, bevel=0.02)
bpy.ops.mesh.primitive_cube_add(size=1, location=(0, -0.62, 0.6))
mouth = bpy.context.object
mouth.scale = (0.38, 0.02, 0.28)
finish(mouth, "MoltenMouth", glow)

# Ore input hopper on the back (+Y): a tapered funnel.
bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.45, radius2=0.15, depth=0.5,
                                location=(-0.3, 0.8, 1.55), rotation=(math.pi, 0, math.pi / 4))
finish(bpy.context.object, "Hopper", hazard, bevel=0.01)
bpy.ops.mesh.primitive_cube_add(size=1, location=(-0.3, 0.62, 1.15))
chute = bpy.context.object
chute.scale = (0.2, 0.4, 0.3)
finish(chute, "Chute", dark)

# Output tray on the front.
bpy.ops.mesh.primitive_cube_add(size=1, location=(0, -0.95, 0.3))
tray = bpy.context.object
tray.scale = (0.5, 0.4, 0.06)
finish(tray, "OutputTray", steel, bevel=0.015)

# Parent everything to one root so it imports as a single prop in Godot.
root = bpy.data.objects.new("Smelter", None)
scene.collection.objects.link(root)
for obj in list(scene.objects):
    if obj is not root:
        obj.parent = root

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_DIR, "smelter.blend"))
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT_DIR, "smelter.glb"),
                          export_format="GLB", export_apply=True)

if RENDER:
    # Camera, lights and a floor for a preview render (not exported).
    bpy.ops.mesh.primitive_plane_add(size=20, location=(0, 0, 0))
    finish(bpy.context.object, "Floor", material("Floor", (0.2, 0.2, 0.22), roughness=0.9))

    bpy.ops.object.camera_add(location=(3.6, -4.2, 2.8))
    cam = bpy.context.object
    target = bpy.data.objects.new("CamTarget", None)
    target.location = (0, 0, 0.9)
    scene.collection.objects.link(target)
    track = cam.constraints.new("TRACK_TO")
    track.target = target
    scene.camera = cam

    bpy.ops.object.light_add(type="SUN", location=(0, 0, 5), rotation=(0.8, 0.2, 0.9))
    bpy.context.object.data.energy = 3.0
    bpy.ops.object.light_add(type="AREA", location=(-3, -2, 3))
    bpy.context.object.data.energy = 300

    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.05, 0.06, 0.08, 1)
    scene.world = world

    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 64
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 960
    scene.render.resolution_y = 720
    scene.render.filepath = os.path.join(OUT_DIR, "smelter_preview.png")
    bpy.ops.render.render(write_still=True)

print("Wrote smelter assets to", OUT_DIR)
