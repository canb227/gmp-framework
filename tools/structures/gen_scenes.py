"""
Writes the structure scenes (.tscn) for the model families, their models' .import files, and checks that no
solid collider leaves the structure's cells.

    python3 tools/structures/gen_scenes.py            # generate everything, then validate
    python3 tools/structures/gen_scenes.py --check    # validate the existing scenes only

Scene definitions live in scenes_*.py next to this file; shared helpers in scenegen.py.
"""
import os, re, sys, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scenegen import REPO, mat_mul, mat_vec, add, IDENT
import scenes_conveyors

FAMILIES = [
    ("conveyor additions", scenes_conveyors.basic_extras),
    ("advanced conveyors", scenes_conveyors.advanced),
    ("magnetic conveyors", scenes_conveyors.magnetic),
]
for _mod, _fams in (("scenes_chutes", ["basic", "advanced"]), ("scenes_machines", ["launchers", "sorting", "fields", "processing", "props"])):
    try:
        _m = __import__(_mod)
        FAMILIES += [(f"{_mod.split('_')[1]} {name}", getattr(_m, name)) for name in _fams]
    except ModuleNotFoundError:
        pass

# ---------------------------------------------------------------------------- validation
NUM = r"-?[\d.]+(?:e-?\d+)?"

def nums(text):
    """Numbers inside a Vector3(...) / Transform3D(...) / PackedVector3Array(...) literal."""
    return [float(x) for x in re.findall(NUM, text.split("(", 1)[1])]

def parse_xf(props):
    if "transform" in props:
        v = nums(props["transform"])
        return [v[0:3], v[3:6], v[6:9]], tuple(v[9:12])
    if "position" in props:
        return IDENT, tuple(nums(props["position"]))
    return IDENT, (0.0, 0.0, 0.0)

def parse(path):
    nodes, cur = [], None
    for line in open(path, encoding="utf-8"):
        line = line.strip()
        if line.startswith("[node "):
            cur = {"hdr": line, "props": {}}
            cur["name"] = re.search(r'name="([^"]+)"', line).group(1)
            m = re.search(r'parent="([^"]+)"', line)
            cur["parent"] = m.group(1) if m else None
            m = re.search(r'type="([^"]+)"', line)
            cur["type"] = m.group(1) if m else None
            nodes.append(cur)
        elif cur is not None and " = " in line:
            k, v = line.split(" = ", 1)
            cur["props"][k] = v
    return nodes

def check(path, tol=0.002):
    nodes = parse(path)
    root = nodes[0]
    cells = [(0, 0, 0)]
    if "cellOffsets" in root["props"]:
        cells = [tuple(int(x) for x in t) for t in re.findall(r"Vector3i\((-?\d+), (-?\d+), (-?\d+)\)", root["props"]["cellOffsets"])]
    def inside(p):
        return any(all(2 * c[i] - 1 - tol <= p[i] <= 2 * c[i] + 1 + tol for i in range(3)) for c in cells)
    frames = {".": (IDENT, (0.0, 0.0, 0.0))}
    sensors = set()
    for n in nodes[1:]:
        if n["type"] == "Box3DBody" and n["parent"] is not None:
            m, o = parse_xf(n["props"])
            pm, po = frames.get(n["parent"], (IDENT, (0, 0, 0)))
            key = n["name"] if n["parent"] == "." else n["parent"] + "/" + n["name"]
            frames[key] = (mat_mul(pm, m), add(mat_vec(pm, o), po))
            if n["props"].get("is_sensor") == "true":
                sensors.add(key)
    bad = []
    for n in nodes[1:]:
        if n["type"] != "Box3DCollisionShape" or n["parent"] in sensors:
            continue
        size = nums(n["props"]["box_size"])
        m, o = parse_xf(n["props"])
        pm, po = frames.get(n["parent"], (IDENT, (0, 0, 0)))
        for sx in (-0.5, 0.5):
            for sy in (-0.5, 0.5):
                for sz in (-0.5, 0.5):
                    local = add(mat_vec(m, (sx * size[0], sy * size[1], sz * size[2])), o)
                    p = add(mat_vec(pm, local), po)
                    if not inside(p):
                        bad.append((n["name"], tuple(round(x, 3) for x in p)))
                        break
                else:
                    continue
                break
    if "mesh_vertices" in root["props"]:
        v = nums(root["props"]["mesh_vertices"])
        for i in range(0, len(v), 3):
            if not inside(v[i:i + 3]):
                bad.append(("root mesh", tuple(round(x, 3) for x in v[i:i + 3]))); break
    return bad

def main():
    written = []
    if "--check" not in sys.argv:
        for label, fn in FAMILIES:
            out = fn()
            written += out
            print(f"{label}: {len(out)} scenes")
    else:
        for d, _, fs in os.walk(os.path.join(REPO, "game", "scenes", "structures")):
            written += [os.path.relpath(os.path.join(d, x), REPO) for x in fs if x.endswith(".tscn")]
    problems = 0
    for rel in written:
        bad = check(os.path.join(REPO, rel))
        if bad:
            problems += 1
            print(f"  OUTSIDE CELLS {rel}: " + "; ".join(f"{n} {p}" for n, p in bad[:4]) + (" ..." if len(bad) > 4 else ""))
    print(f"{len(written)} scenes, {problems} with colliders outside their cells")

if __name__ == "__main__":
    main()
