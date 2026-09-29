"""
Decor prop scenes: game/scenes/decor/<Name>.tscn for every model in game/assets/models/props/decor/ (built by
tools/blender/run.py build decor). Props are non-functional: a Node3D root with the model (its "idle-loop"
autoplaying when it has one) and, for solid props, a static Box3DBody sized to the model's bounds (or to the
hand-set boxes in CUSTOM). Origin on the floor, fronts toward +Z.

    python3 tools/decor/gen_decor.py
"""
import os, sys, json, struct
sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "structures"))
from scenegen import REPO, ensure_import, new_uid, f, b2g, xform_text, mat_mul, rot_x
import math

MODELS = "res://game/assets/models/props/decor/"
IMPORT = "res://game/assets/models/shared/salvage_import.gd"
OUT = os.path.join(REPO, "game", "scenes", "decor")

SOLID = {"cracked_pillar", "collapsed_pillar", "rubble_pile", "moss_mound", "collapsed_panel_wall", "lab_bench", "flicker_terminal",
         "monitor_bank", "filing_cabinets", "cryo_pod", "observation_booth", "pipe_cluster", "rusted_barrels", "crate_stack",
         "tipped_barriers", "elevator_ruin"}
CUSTOM = {   # name -> [(centre, size)] in Godot units (y up), for props whose bounds would be a poor collider
    "overgrown_tree": [((0, 2.5, 0), (0.9, 5.0, 0.9))],
    "broken_catwalk": [((-2.0, 3.0, 0), (4.0, 0.1, 1.4))] + [((x, 1.5, z), (0.15, 3.0, 0.15)) for x in (-3.5, -0.5) for z in (-0.6, 0.6)],
    "gantry_crane": [((sx * 25, 16, sz * 6), (1.4, 32, 1.4)) for sx in (-1, 1) for sz in (-1, 1)],
    "reactor_sphere": [((0, 18, 0), (20, 20, 20))],
    "arcology_spire": [((0, 36, 0), (8, 72, 8))],
    "cooling_tower": [],
    "sky_bridge": [((x, 20, 0), (8, 40, 8)) for x in (-40, 40)],
    "panel_arm_wall": [((0, 13, -1.5), (38, 26, 1.0))],
}

# Level kit colliders, in the kit's Blender frame: (centre, size (x, y, z), tilt about X in degrees)
_SLOPE = math.degrees(math.atan2(2, 4))
_WALL_X = lambda x: ((x, 0, 2.0), (0.3, 4.0, 4.0), 0)
KIT = {
    "kit_floor": [((0, 0, -0.15), (4, 4, 0.3), 0)],
    "kit_floor_cracked": [((0, 0, -0.15), (4, 4, 0.3), 0)],
    "kit_wall": [((0, 0, 2), (4, 0.3, 4), 0)],
    "kit_wall_damaged": [((0, 0, 2), (4, 0.3, 4), 0)],
    "kit_wall_window": [((0, 0, 2), (4, 0.3, 4), 0)],
    "kit_doorway": [((-1.5, 0, 2), (1, 0.3, 4), 0), ((1.5, 0, 2), (1, 0.3, 4), 0), ((0, 0, 3.5), (2, 0.3, 1), 0)],
    "kit_hallway": [((0, 0, -0.15), (4, 4, 0.3), 0), _WALL_X(-1.85), _WALL_X(1.85), ((0, 0, 4.15), (4, 4, 0.3), 0)],
    "kit_hallway_broken": [((0, 0, -0.15), (4, 4, 0.3), 0), _WALL_X(-1.85), _WALL_X(1.85), ((0, 0, 4.15), (4, 4, 0.3), 0)],
    "kit_hallway_corner": [((0, 0, -0.15), (4, 4, 0.3), 0), _WALL_X(-1.85), ((0, 1.85, 2), (4, 0.3, 4), 0), ((0, 0, 4.15), (4, 4, 0.3), 0)],
    "kit_catwalk": [((0, 0, -0.06), (1.6, 4, 0.12), 0), ((-0.8, 0, 0.5), (0.05, 4, 1), 0), ((0.8, 0, 0.5), (0.05, 4, 1), 0)],
    "kit_catwalk_corner": [((0, 0, -0.06), (1.6, 1.6, 0.12), 0), ((-0.8, 0, 0.5), (0.05, 1.6, 1), 0), ((0, 0.8, 0.5), (1.6, 0.05, 1), 0)],
    "kit_catwalk_stairs": [((0, 0, 0.96), (1.5, 4.47, 0.08), _SLOPE), ((-0.8, 0, 1.5), (0.05, 4.47, 1), _SLOPE), ((0.8, 0, 1.5), (0.05, 4.47, 1), _SLOPE)],
    "kit_catwalk_support": [((0, 0, -2), (0.2, 0.2, 4), 0)],
    "kit_stairs": [((0, 0, 0.9), (2.4, 4.47, 0.2), _SLOPE)],
    "kit_column": [((0, 0, 2), (1, 1, 4), 0)],
    "kit_railing": [((0, 0, 0.5), (4, 0.1, 1), 0)],
}

def kit_box(c, size, tilt):
    """Kit collider (Blender frame) -> Godot centre, size and basis."""
    g = b2g(c)
    gsize = (size[0], size[2], size[1])
    return g, gsize, rot_x(math.radians(tilt))

def glb_json(path):
    b = open(path, "rb").read()
    ln = struct.unpack("<I", b[12:16])[0]
    return json.loads(b[20:20 + ln])

def bounds(j):
    """Axis-aligned bounds (glTF = Godot axes) of every mesh node, using node translation and scale (the decor
    models keep rotations baked into their vertices)."""
    lo, hi = [1e9] * 3, [-1e9] * 3
    def walk(i, off, sc):
        n = j["nodes"][i]
        t = n.get("translation", [0, 0, 0]); s = n.get("scale", [1, 1, 1])
        o = [off[k] + t[k] * sc[k] for k in range(3)]; s2 = [sc[k] * s[k] for k in range(3)]
        if "mesh" in n:
            for pr in j["meshes"][n["mesh"]]["primitives"]:
                a = j["accessors"][pr["attributes"]["POSITION"]]
                for k in range(3):
                    lo[k] = min(lo[k], o[k] + a["min"][k] * s2[k]); hi[k] = max(hi[k], o[k] + a["max"][k] * s2[k])
        for c in n.get("children", []):
            walk(c, o, s2)
    for i in j["scenes"][0]["nodes"]:
        walk(i, [0, 0, 0], [1, 1, 1])
    return lo, hi

def pascal(name):
    return "".join(w.capitalize() for w in name.split("_"))

def main():
    for folder, out, models in (("decor", OUT, MODELS), ("kit", os.path.join(OUT, "kit"), "res://game/assets/models/props/kit/")):
        write_folder(folder, out, models)

def write_folder(folder, out, models):
    os.makedirs(out, exist_ok=True)
    src = os.path.join(REPO, "game", "assets", "models", "props", folder)
    names = sorted(fn[:-4] for fn in os.listdir(src) if fn.endswith(".glb"))
    for name in names:
        res = models + name + ".glb"
        uid = ensure_import(res, IMPORT)
        j = glb_json(os.path.join(src, name + ".glb"))
        animated = bool(j.get("animations"))
        boxes = CUSTOM.get(name)
        if boxes is None and name in SOLID:
            lo, hi = bounds(j)
            lo[1] = max(lo[1], 0.0)
            boxes = [(tuple((lo[k] + hi[k]) / 2 for k in range(3)), tuple(hi[k] - lo[k] for k in range(3)))]
        scene_res = "res://" + os.path.relpath(os.path.join(out, pascal(name) + ".tscn"), REPO).replace(os.sep, "/")
        dst = os.path.join(out, pascal(name) + ".tscn")
        old = open(dst, encoding="utf-8").readline() if os.path.exists(dst) else ""
        suid = old.split('uid="', 1)[1].split('"', 1)[0] if 'uid="' in old else new_uid(scene_res)   # keep uids stable
        lines = [f'[gd_scene format=3 uid="{suid}"]', "",
                 f'[ext_resource type="PackedScene" uid="{uid}" path="{res}" id="1_model"]', "",
                 f'[node name="{pascal(name)}" type="Node3D"]', "",
                 '[node name="Model" parent="." instance=ExtResource("1_model")]', ""]
        if animated:
            lines += ['[node name="AnimationPlayer" parent="Model"]', 'autoplay = "idle-loop"', ""]
        placed = [(c, size, None) for c, size in (boxes or [])] + [kit_box(*b) for b in KIT.get(name, [])]
        for k, (c, size, basis) in enumerate(placed):
            where = f"transform = {xform_text(basis, c)}" if basis and abs(basis[1][1] - 1) > 1e-6 else f"position = Vector3({f(c[0])}, {f(c[1])}, {f(c[2])})"
            lines += [f'[node name="Collider{k}" type="Box3DBody" parent="."]', "body_type = 0",
                      f"box_size = Vector3({f(size[0])}, {f(size[1])}, {f(size[2])})", where, ""]
        open(os.path.join(out, pascal(name) + ".tscn"), "w", newline="\n").write("\n".join(lines))
        print(f"{folder}/{pascal(name):22s} {'animated' if animated else '        '} {len(placed)} collider(s)")

if __name__ == "__main__":
    main()
