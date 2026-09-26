"""
Renders the 128x128 inventory icons from the conveyor/prop collections in the open Blender file (conveyors.blend
or props.blend with both sets built). Blueprint icons get the blue blueprint card, items a dark card.

    exec(open(r"...\\props\\source\\render_icons.py").read(), {"__name__": "__main__"})
"""
import bpy, math, os
import numpy as np
from mathutils import Vector

ROOT = r"C:\Users\steph\OneDrive\Documents\godot\projects\gmp-framework\game\assets\icons"
SIZE = 128

# collection -> (output file, card style, view direction from the model (Blender: +Y is the front))
ICONS = [
    ("Conveyor_Straight", "blueprints/blueprint_conveyor.png", "blueprint", (1.3, 1.0, 1.05)),
    ("Conveyor_Slope", "blueprints/blueprint_conveyor_slope.png", "blueprint", (1.6, 0.9, 1.0)),
    ("Conveyor_TurnRight", "blueprints/blueprint_conveyor_turn_right.png", "blueprint", (-0.35, -0.9, 1.6)),
    ("Conveyor_TurnLeft", "blueprints/blueprint_conveyor_turn_left.png", "blueprint", (0.35, -0.9, 1.6)),
    ("Prop_Grinder", "blueprints/blueprint_grinder.png", "blueprint", (1.2, 1.3, 0.8)),
    ("Prop_Spawner", "blueprints/blueprint_item_spawner.png", "blueprint", (1.0, 1.4, 0.8)),
    ("Prop_Void", "blueprints/blueprint_item_void.png", "blueprint", (0.9, -1.3, 1.3)),
    ("Prop_Magnet", "items/magnet_rod.png", "item", (1.6, 0.6, 0.7)),
    ("Ore_Iron", "items/iron_ore.png", "item", (1.2, 1.3, 0.9)),
    ("Ore_IronGround", "items/iron_ore_ground.png", "item", (1.2, 1.3, 1.0)),
    ("Ore_Copper", "items/copper_ore.png", "item", (1.2, 1.3, 0.9)),
    ("Ore_CopperGround", "items/copper_ore_ground.png", "item", (1.2, 1.3, 1.0)),
]

def card(style):
    """Background as an sRGB float array (H, W, 3), row 0 at the bottom like Blender's pixels."""
    y = np.linspace(0, 1, SIZE)[:, None]
    x = np.linspace(0, 1, SIZE)[None, :]
    if style == "blueprint":
        top, bot = np.array([0.13, 0.30, 0.62]), np.array([0.05, 0.14, 0.38])
    else:
        top, bot = np.array([0.24, 0.24, 0.26]), np.array([0.10, 0.10, 0.11])
    bg = bot + (top - bot) * y[..., None]
    bg = np.broadcast_to(bg, (SIZE, SIZE, 3)).copy()
    # soft vignette
    r = np.sqrt((x - 0.5) ** 2 + (y - 0.5) ** 2)
    bg *= (1.0 - 0.35 * np.clip(r * 1.4 - 0.2, 0, 1))[..., None]
    if style == "blueprint":
        grid = np.zeros((SIZE, SIZE))
        grid[::16, :] = 1; grid[:, ::16] = 1
        grid[::64, :] = 2; grid[:, ::64] = 2
        bg += (grid[..., None] * 0.035) * np.array([0.7, 0.85, 1.0])
    return np.clip(bg, 0, 1)

def frame(coll, cam, direction):
    pts = []
    dg = bpy.context.evaluated_depsgraph_get()
    for ob in coll.objects:
        if ob.type != 'MESH':
            continue
        me = ob.evaluated_get(dg).data
        pts += [ob.matrix_world @ v.co for v in me.vertices]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    ctr = (lo + hi) / 2
    d = Vector(direction).normalized()
    cam.location = ctr + d * 20
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    # ortho scale: the model's extent across the view plane
    right = (-d).cross(Vector((0, 0, 1))).normalized()
    up = right.cross(-d).normalized()
    ext = max(max(abs((p - ctr).dot(right)) for p in pts), max(abs((p - ctr).dot(up)) for p in pts))
    cam.data.ortho_scale = ext * 2 * 1.12

def render_icons():
    sc = bpy.context.scene
    lc = sc.view_layers[0].layer_collection.children
    colls = [c.name for c in lc]
    state = {n: lc[n].exclude for n in colls}
    cam = bpy.data.objects.get("IconCam") or bpy.data.objects.new("IconCam", bpy.data.cameras.new("IconCam"))
    if cam.name not in sc.collection.objects:
        sc.collection.objects.link(cam)
    cam.data.type = 'ORTHO'
    key = bpy.data.objects.get("IconKey") or bpy.data.objects.new("IconKey", bpy.data.lights.new("IconKey", 'SUN'))
    if key.name not in sc.collection.objects:
        sc.collection.objects.link(key)
    key.data.energy = 3.2
    key.rotation_euler = (math.radians(45), math.radians(15), math.radians(-40))
    old = (sc.camera, sc.render.resolution_x, sc.render.resolution_y, sc.render.film_transparent, sc.render.filepath)
    sc.camera = cam
    sc.render.resolution_x = sc.render.resolution_y = SIZE
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = True
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    world = sc.world
    wcol = tuple(world.node_tree.nodes["Background"].inputs[0].default_value)
    wstr = world.node_tree.nodes["Background"].inputs[1].default_value
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.57, 0.62, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
    tmp = os.path.join(bpy.app.tempdir, "icon_tmp.png")
    done = []
    try:
        for cname, rel, style, direction in ICONS:
            if cname not in colls:
                continue
            for n in colls:
                lc[n].exclude = n != cname
            frame(bpy.data.collections[cname], cam, direction)
            sc.render.filepath = tmp
            bpy.ops.render.render(write_still=True)
            img = bpy.data.images.load(tmp, check_existing=False)
            px = np.array(img.pixels[:]).reshape(SIZE, SIZE, 4)
            bpy.data.images.remove(img)
            a = px[..., 3:4]
            out = np.concatenate([px[..., :3] * a + card(style) * (1 - a), np.ones((SIZE, SIZE, 1))], axis=2)
            res = bpy.data.images.new("icon_out", SIZE, SIZE, alpha=True)
            res.pixels[:] = out.astype(np.float32).ravel()
            res.filepath_raw = os.path.join(ROOT, rel)
            res.file_format = 'PNG'
            res.save()
            bpy.data.images.remove(res)
            done.append(rel)
    finally:
        for n, ex in state.items():
            lc[n].exclude = ex
        sc.camera, sc.render.resolution_x, sc.render.resolution_y, sc.render.film_transparent, sc.render.filepath = old
        world.node_tree.nodes["Background"].inputs[0].default_value = wcol
        world.node_tree.nodes["Background"].inputs[1].default_value = wstr
        bpy.data.objects.remove(key, do_unlink=True)
        bpy.data.objects.remove(cam, do_unlink=True)
    return done

if __name__ == "__main__":
    print(render_icons())
