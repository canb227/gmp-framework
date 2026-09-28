"""
Headless driver for the model build scripts (needs Blender's Python module: pip install bpy==4.5.14).

    python3 tools/blender/run.py list
    python3 tools/blender/run.py build   <family> [<family> ...] [--no-export] [--save-blend] [--only CollA,CollB]
    python3 tools/blender/run.py preview <family> <out_dir> [--only NameA,NameB] [--view x,y,z] [--samples N] [--hide Node,Node]
    python3 tools/blender/run.py icons   <family> [<family> ...] [--only NameA,NameB]

build     rebuilds a family and exports its .glb files next to the family's source folder (--save-blend also
          writes <family>.blend into the source folder, like conveyors.blend / props.blend).
preview   renders each built collection to <out_dir>/<Collection>.png for review.
icons     renders the 128 px blueprint icons listed in each family's ICONS (see render_icons.py for the style).

Every family script is a plain Blender script with a build_all(do_export) that returns
[(collection, glb filename, tag)], so it can also be run from Blender's text editor.
"""
import os, sys, math

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
M = os.path.join(REPO, "game", "assets", "models")
FAMILIES = {
    "conveyors": os.path.join(M, "conveyors", "source", "build_conveyors.py"),
    "props": os.path.join(M, "props", "source", "build_props.py"),
    "conveyor_extras": os.path.join(M, "conveyors", "source", "build_conveyor_extras.py"),
    "conveyors_advanced": os.path.join(M, "conveyors_advanced", "source", "build_conveyors_advanced.py"),
    "conveyors_magnetic": os.path.join(M, "conveyors_magnetic", "source", "build_conveyors_magnetic.py"),
    "chutes": os.path.join(M, "chutes", "source", "build_chutes.py"),
    "launchers": os.path.join(M, "machines", "launchers", "source", "build_launchers.py"),
    "sorting": os.path.join(M, "machines", "sorting", "source", "build_sorting.py"),
    "fields": os.path.join(M, "machines", "fields", "source", "build_fields.py"),
    "processing": os.path.join(M, "machines", "processing", "source", "build_processing.py"),
    "concepts": os.path.join(M, "machines", "concepts", "source", "build_concepts.py"),
    "tools": os.path.join(M, "tools", "source", "build_tools.py"),
}
ICON_ROOT = os.path.join(REPO, "game", "assets", "icons")

def load(family):
    import bpy
    path = FAMILIES[family]
    # "conveyor_lib" keeps build_conveyors.py from building on load; the other scripts only build under __main__
    ns = {"__name__": "conveyor_lib", "__file__": path}
    exec(compile(open(path, encoding="utf-8").read(), path, "exec"), ns)
    return ns

def fresh():
    import bpy
    bpy.ops.wm.read_factory_settings(use_empty=True)

def export_only(ns, built, only):
    """Export just the named collections (plain node names, as build_all would)."""
    for coll, glb, tag in built:
        if coll.name not in only:
            continue
        for ob in coll.objects: ob.name = ob.name.split("__", 1)[1]
        if "export_glb" in ns:
            ns["export_glb"](coll, os.path.join(ns["OUT_DIR"], glb))
        else:
            ns["export"](coll, glb)
        for ob in coll.objects: ob.name = f"{tag}__{ob.name}"
        print(f"exported {glb}")

def build(families, export=True, save=False, only=None):
    import bpy
    out = []
    for fam in families:
        fresh()
        ns = load(fam)
        built = ns["build_all"](do_export=export and not only)
        if export and only:
            export_only(ns, built, only)
        out.append((fam, ns, built))
        print(f"[{fam}] built {len(built)} pieces" + (" and exported" if export and not only else ""))
        if save:
            dst = os.path.join(os.path.dirname(FAMILIES[fam]), f"{fam}.blend")
            bpy.ops.wm.save_as_mainfile(filepath=dst, compress=True)
            print(f"[{fam}] saved {dst}")
    return out

def bounds(coll):
    from mathutils import Vector
    pts = []
    import bpy
    dg = bpy.context.evaluated_depsgraph_get()
    for ob in coll.objects:
        if ob.type == 'MESH':
            me = ob.evaluated_get(dg).data
            pts += [ob.matrix_world @ v.co for v in me.vertices]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi, pts

def isolate(coll_name):
    import bpy
    lc = bpy.context.scene.view_layers[0].layer_collection.children
    for c in lc:
        c.exclude = c.name != coll_name

def preview(family, out_dir, only=None, view=(1.2, 1.3, 0.8), samples=24, hide=()):
    import bpy
    from mathutils import Vector
    os.makedirs(out_dir, exist_ok=True)
    (_, ns, built), = build([family], export=False)
    sc = bpy.context.scene
    floor_me = bpy.data.meshes.new("PreviewFloor")
    floor_me.from_pydata([(-60, -60, 0), (60, -60, 0), (60, 60, 0), (-60, 60, 0)], [], [(0, 1, 2, 3)])
    fm = bpy.data.materials.new("PreviewFloor"); fm.use_nodes = True
    fm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.16, 0.16, 0.17, 1)
    floor_me.materials.append(fm)
    floor = bpy.data.objects.new("PreviewFloor", floor_me); sc.collection.objects.link(floor)
    cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam")); sc.collection.objects.link(cam)
    cam.data.lens = 40; sc.camera = cam
    sun = bpy.data.objects.new("PreviewSun", bpy.data.lights.new("PreviewSun", 'SUN')); sc.collection.objects.link(sun)
    sun.data.energy = 3.2; sun.rotation_euler = (math.radians(45), math.radians(15), math.radians(-40))
    world = bpy.data.worlds.new("PreviewWorld"); world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.57, 0.62, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.6
    sc.world = world
    sc.render.engine = "CYCLES"; sc.cycles.device = "CPU"; sc.cycles.samples = samples; sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = 800, 600
    d = Vector(view).normalized()
    for coll, glb, tag in built:
        if only and coll.name not in only:
            continue
        for ob in coll.objects:
            ob.hide_render = ob.name.split("__")[-1] in hide
        isolate(coll.name)
        lo, hi, pts = bounds(coll)
        print(f"BOUNDS {coll.name}: {tuple(round(v, 3) for v in lo)} .. {tuple(round(v, 3) for v in hi)}")
        floor.location.z = lo.z - 0.002
        ctr = (lo + hi) / 2
        right = (-d).cross(Vector((0, 0, 1))).normalized(); up = right.cross(-d).normalized()
        ext = max(max(abs((p - ctr).dot(right)) for p in pts), max(abs((p - ctr).dot(up)) for p in pts) * 800 / 600)
        dist = ext / math.tan(cam.data.angle / 2) * 1.08 + (hi - lo).length * 0.2
        cam.location = ctr + d * dist
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(out_dir, f"{coll.name}.png")
        bpy.ops.render.render(write_still=True)
        print("rendered", sc.render.filepath)

def icons(families, only=None):
    import bpy
    ri = os.path.join(M, "props", "source", "render_icons.py")
    R = {"__name__": "render_icons_lib", "__file__": ri}
    exec(compile(open(ri, encoding="utf-8").read(), ri, "exec"), R)
    for fam in families:
        (_, ns, built), = build([fam], export=False)
        if "icon_collections" in ns:
            ns["icon_collections"]()                 # extra posed copies some families render icons from
        entries = [e for e in (ns.get("ICONS") or list(R["ICONS"])) if not only or e[0] in only]
        R["ROOT"] = ICON_ROOT
        R["ICONS"] = entries
        world = bpy.data.worlds.new("IconWorld"); world.use_nodes = True
        bpy.context.scene.world = world
        bpy.context.scene.render.engine = "CYCLES"
        bpy.context.scene.cycles.device = "CPU"; bpy.context.scene.cycles.samples = 32
        bpy.context.scene.cycles.use_denoising = True
        print(f"[{fam}] icons:", R["render_icons"]())

if __name__ == "__main__":
    args = sys.argv[1:]
    opt = lambda k, d=None: args[args.index(k) + 1] if k in args else d
    cmd = args[0] if args else "list"
    pos = [a for i, a in enumerate(args[1:], 1) if not a.startswith("--") and not args[i - 1].startswith("--")]
    only = set(opt("--only").split(",")) if opt("--only") else None
    if cmd == "list":
        for k, v in FAMILIES.items():
            print(f"{k:20s} {os.path.relpath(v, REPO)}{'' if os.path.exists(v) else '  (missing)'}")
    elif cmd == "build":
        build(pos, export="--no-export" not in args, save="--save-blend" in args, only=only)
    elif cmd == "preview":
        view = tuple(float(v) for v in opt("--view", "1.2,1.3,0.8").split(","))
        preview(pos[0], pos[1], only, view, int(opt("--samples", "24")), tuple(opt("--hide", "").split(",")))
    elif cmd == "icons":
        icons(pos, only)
