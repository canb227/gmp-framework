"""
Writes stress_test.tscn: a 100 x 100 x 30 m room packed with a late-game factory for performance testing.

Run from the project root:  python game/scenes/levels/stress_test/make_stress_test.py
It overwrites stress_test.tscn, so make layout changes here rather than in the editor (or stop using this script).

Belts are laid by a "turtle": each path keeps the cell its next piece starts in (the left lane, looking along the
belt) and the way it is heading, and each piece moves it on. Every piece's cells are read from its scene, and the
script stops if two structures would share a cell.
"""
import math
import os
import re

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", "..", "..", ".."))
OUT = os.path.join(os.path.dirname(__file__), "stress_test.tscn")

BELTS = "res://game/scenes/structures/logistics/conveyors/basic/"
SCENES = {
    "S": BELTS + "Conveyor.tscn",
    "L": BELTS + "ConveyorTurnLeft.tscn",
    "R": BELTS + "ConveyorTurnRight.tscn",
    "U": BELTS + "ConveyorSlopeUp.tscn",
    "D": BELTS + "ConveyorSlopeDown.tscn",
    "T": "res://game/scenes/structures/sorting/Trommel.tscn",
    "X": "res://game/scenes/structures/special/DevItemDeleter.tscn",
    "spawner": "res://game/scenes/structures/special/DevSpawner.tscn",
}

# For each piece, in its own unturned frame: the left-lane cell items come in on and the way they travel in,
# then the cell just past its exit where the next piece starts and the way they travel out.
PIECES = {
    "S": ((0, 0, 0), (0, 0, -1), (0, 0, -2), (0, 0, -1)),
    "L": ((0, 0, -1), (1, 0, 0), (0, 0, -2), (0, 0, -1)),
    "R": ((1, 0, 0), (-1, 0, 0), (0, 0, -2), (0, 0, -1)),
    "U": ((0, 0, 0), (0, 0, -1), (0, 2, -4), (0, 0, -1)),
    "D": ((0, 2, 0), (0, 0, -1), (0, 0, -4), (0, 0, -1)),
    "T": ((0, 2, 0), (0, 0, -1), (0, 0, -8), (0, 0, -1)),
    "X": ((1, 0, 0), (0, 0, -1), None, None),
}

N = (0, 0, -1)
S = (0, 0, 1)
E = (1, 0, 0)
W = (-1, 0, 0)

ITEMS = [
    "sieve_ball_010", "sieve_ball_025", "sieve_ball_050", "sieve_ball_075",
    "sieve_cube_010", "sieve_cube_025", "sieve_cube_050", "sieve_cube_075",
    "scrap_ball", "electronic_bits", "scrap_ingot",
]

# What the spawners put out: no spheres.
SPAWN_ITEMS = [
    "sieve_cube_010", "sieve_cube_025", "sieve_cube_050", "sieve_cube_075",
    "electronic_bits", "scrap_ingot",
]

SPAWN_PER_SECOND = 8.0   # each line's spawner
DELETE_PER_SECOND = 7.0  # what each line's end can delete, a little less than it gets


def add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def scale(a, k):
    return (a[0] * k, a[1] * k, a[2] * k)


def rot(v, q):
    """BuildGrid.RotateOffset: q quarter turns of +90 degree yaw."""
    x, y, z = v
    for _ in range(q % 4):
        x, y, z = z, y, -x
    return (x, y, z)


def turns_to(local_dir, world_dir):
    for q in range(4):
        if rot(local_dir, q) == world_dir:
            return q
    raise ValueError(f"no quarter turn takes {local_dir} to {world_dir}")


def read_cells(res_path):
    path = os.path.join(ROOT, res_path.replace("res://", ""))
    with open(path, encoding="utf-8") as f:
        text = f.read()
    block = re.search(r"^cells = Dictionary\[Vector3i, int\]\(\{(.*?)\}\)", text, re.S | re.M)
    if block is None:
        return [(0, 0, 0)]
    return [tuple(int(n) for n in m) for m in re.findall(r"Vector3i\((-?\d+), (-?\d+), (-?\d+)\)", block.group(1))]


def fmt(x):
    if abs(x - round(x)) < 1e-6:
        return str(int(round(x)))
    return f"{x:.5g}"


def transform(basis_rows, origin):
    """A tscn Transform3D: the basis by rows, then the origin."""
    values = [v for row in basis_rows for v in row] + list(origin)
    return "Transform3D(" + ", ".join(fmt(v) for v in values) + ")"


def yaw_rows(q):
    a = q * math.pi / 2
    c = round(math.cos(a))
    s = round(math.sin(a))
    return ((c, 0, s), (0, 1, 0), (-s, 0, c))


def placement(anchor, q):
    """Structure.PlacementTransform: turns about the anchor cell's centre, root at its left-bottom-back corner."""
    rows = yaw_rows(q)
    corner = (-0.5, -0.5, 0.5)
    turned = tuple(sum(rows[i][j] * corner[j] for j in range(3)) for i in range(3))
    origin = (anchor[0] + 0.5 + turned[0], anchor[1] + 0.5 + turned[1], anchor[2] + 0.5 + turned[2])
    return transform(rows, origin)


def weights(items):
    return "Dictionary[String, float]({\n" + ",\n".join(f'"{i}": 1.0' for i in items) + "\n})"


class Factory:
    def __init__(self):
        self.cells = {kind: read_cells(path) for kind, path in SCENES.items()}
        self.taken = {}
        self.nodes = []   # (parent, name, kind, anchor, q, extra lines, child overrides)
        self.counts = {}
        self.lo = [10**9] * 3
        self.hi = [-10**9] * 3

    def place(self, parent, name, kind, anchor, q, extra=(), children=()):
        for c in self.cells[kind]:
            cell = add(anchor, rot(c, q))
            if cell in self.taken:
                raise SystemExit(f"{name} ({kind}) would overlap {self.taken[cell]} at cell {cell}")
            self.taken[cell] = name
            for i in range(3):
                self.lo[i] = min(self.lo[i], cell[i])
                self.hi[i] = max(self.hi[i], cell[i])
        self.nodes.append((parent, name, kind, anchor, q, list(extra), list(children)))
        self.counts[kind] = self.counts.get(kind, 0) + 1


class Path:
    def __init__(self, factory, name, start, heading):
        self.f = factory
        self.name = name
        self.cell = start
        self.heading = heading
        self.n = 0

    def next_name(self, what):
        self.n += 1
        return f"{self.name}_{self.n:03d}_{what}"

    def spawner(self, items, per_second=SPAWN_PER_SECOND):
        """A DevSpawner hanging over the start of the next piece, dropping items onto its middle."""
        q = turns_to(N, self.heading)
        anchor = add(self.cell, rot((0, 3, 0), q))
        extra = [
            f"itemWeights = {weights(items)}",
            "spawning = true",
            f"spawnInterval = {1.0 / per_second:.4f}",
        ]
        # Out of the bottom of its box, over the middle of a 2 x 2 belt.
        children = [("Output", ["transform = Transform3D(1, 0, 0, 0, 0, 1, 0, -1, 0, 1, -1.5, -1)"])]
        self.f.place("Machines", self.next_name("Spawner"), "spawner", anchor, q, extra, children)

    def run(self, plan):
        for token in plan.split():
            kind, _, count = token.partition("*")
            for _ in range(int(count or 1)):
                self.piece(kind)

    def piece(self, kind):
        in_ref, in_dir, out_ref, out_dir = PIECES[kind]
        q = turns_to(in_dir, self.heading)
        anchor = sub(self.cell, rot(in_ref, q))
        if kind in ("T", "X"):
            parent = "Machines"
        else:
            parent = "Belts"
        self.f.place(parent, self.next_name(kind), kind, anchor, q, self.extra_for(kind))
        if kind == "T":
            self.trommel_fines(anchor, q)
        if out_ref is not None:
            self.cell = add(anchor, rot(out_ref, q))
            self.heading = rot(out_dir, q)

    def extra_for(self, kind):
        if kind == "X":
            return [f"itemsPerSecond = {self.delete_rate}"]
        return []

    delete_rate = DELETE_PER_SECOND

    def end(self, per_second):
        """A deleter catching what comes off the end."""
        self.delete_rate = per_second
        self.piece("X")

    def skip(self, cells):
        self.cell = add(self.cell, scale(self.heading, cells))

    # What the deleters at the next trommel's side delete a second (0: everything, a void).
    fines_rate = DELETE_PER_SECOND / 4

    def trommel_fines(self, anchor, q):
        """Two deleters along the trommel's right side, open toward it, catching what falls through the drum."""
        for z in (-3, -7):
            self.f.place("Machines", self.next_name("FinesDeleter"), "X", add(anchor, rot((4, 0, z), q)), (q + 3) % 4,
                         [f"itemsPerSecond = {self.fines_rate}"])


def build():
    f = Factory()

    # A: a ground-level serpentine through the south half, with a placeholder machine halfway (it deletes what
    # arrives and a spawner starts the next half).
    a = Path(f, "A", (-44, 0, 44), E)
    a.spawner(SPAWN_ITEMS)
    a.run("S*30 L S L S*30 R S R S*30 L S L S*12")
    a.end(DELETE_PER_SECOND)
    a.skip(6)
    a.spawner(SPAWN_ITEMS)
    a.run("S*13 R S R S*30 L S L S*30")
    a.end(DELETE_PER_SECOND)

    # B: a three-lap helix tower in the north-west, then a long run down that pours off its end into the pit.
    b = Path(f, "B", (-40, 0, 0), N)
    b.spawner(SPAWN_ITEMS)
    b.run("S*2")
    for _ in range(3):
        b.run("L S U S " * 4)
    b.run("R S D*7 S*2")

    # C: a high serpentine in the east, crossing over A's turns, stepping down row by row to a trommel. Its fines
    # fall into two deleters at the drum's side, the oversize carries on to a third.
    c = Path(f, "C", (44, 16, 45), W)
    c.spawner(SPAWN_ITEMS)
    c.run("S*15 R R S*15 L L")
    for _ in range(3):
        c.run("S*7 D S*7 R R S*15 L L")
    c.run("S*7 D S*7 R R S*7 D S*7 L S*4 D D D S*2 U T S")
    c.end(DELETE_PER_SECOND / 2)

    # D: a short, fast line south of the pit at 18 items a second (about what the trommel passes), into a trommel
    # hanging over the pit's edge: what falls through the drum goes into voids beside it, the oversize pours out of
    # its chute into the pit.
    d = Path(f, "D", (-1, 0, 23), N)
    d.fines_rate = 0.0
    d.spawner(SPAWN_ITEMS, 18.0)
    d.run("S*2 U T")

    return f


def write(f):
    ext = {kind: str(i + 1) for i, kind in enumerate(SCENES)}
    lines = ["[gd_scene format=3]", ""]
    for kind, path in SCENES.items():
        lines.append(f'[ext_resource type="PackedScene" path="{path}" id="{ext[kind]}"]')
    lines.append('[ext_resource type="Script" path="res://game/scripts/entities/Stirrer.cs" id="stirrer"]')
    lines.append('[ext_resource type="Script" path="res://game/scripts/entities/ItemSeeder.cs" id="seeder"]')
    lines.append("")
    lines.append(STATIC_RESOURCES)
    lines.append(STATIC_NODES)

    lines.append('[node name="Belts" type="Node3D" parent="."]\n')
    lines.append('[node name="Machines" type="Node3D" parent="."]\n')
    for parent, name, kind, anchor, q, extra, children in f.nodes:
        lines.append(f'[node name="{name}" parent="{parent}" instance=ExtResource("{ext[kind]}")]')
        lines.append(f"transform = {placement(anchor, q)}")
        lines.extend(extra)
        lines.append("")
        for child, props in children:
            lines.append(f'[node name="{child}" parent="{parent}/{name}"]')
            lines.extend(props)
            lines.append("")

    lines.append('[node name="ItemSeeder" type="Node3D" parent="." node_paths=PackedStringArray("beltsRoot")]')
    lines.append("transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, -2.5, 0)")
    lines.append('script = ExtResource("seeder")')
    lines.append(f"itemWeights = {weights(ITEMS)}")
    lines.append('beltsRoot = NodePath("../Belts")')
    lines.append("itemsOnBelts = 1700")
    lines.append("itemsInArea = 300")
    lines.append("areaSize = Vector3(13, 2, 13)")
    lines.append("")

    with open(OUT, "w", encoding="utf-8", newline="\n") as out:
        out.write("\n".join(lines))


# ---- the room, pit, stirrer, lights and environment: fixed, written as is ----

def box_node(name, parent, size, center, mesh):
    """A collider and its mesh, as two nodes under the room body."""
    t = transform(((1, 0, 0), (0, 1, 0), (0, 0, 1)), center)
    return (f'[node name="{name}Shape" type="GMPShape" parent="{parent}"]\ntransform = {t}\n'
            f"size = Vector3({fmt(size[0])}, {fmt(size[1])}, {fmt(size[2])})\n\n"
            f'[node name="{name}" type="MeshInstance3D" parent="{parent}"]\ntransform = {t}\n'
            f'mesh = SubResource("{mesh}")\n')


ROOM_BOXES = [
    # name, size, centre, material
    ("FloorNorth", (100, 1, 42), (0, -0.5, -29), "Floor"),
    ("FloorSouth", (100, 1, 42), (0, -0.5, 29), "Floor"),
    ("FloorWest", (42, 1, 16), (-29, -0.5, 0), "Floor"),
    ("FloorEast", (42, 1, 16), (29, -0.5, 0), "Floor"),
    ("PitFloor", (18, 1, 18), (0, -4.5, 0), "Pit"),
    ("PitWallWest", (1, 4, 18), (-8.5, -2, 0), "Pit"),
    ("PitWallEast", (1, 4, 18), (8.5, -2, 0), "Pit"),
    ("PitWallNorth", (16, 4, 1), (0, -2, -8.5), "Pit"),
    ("PitWallSouth", (16, 4, 1), (0, -2, 8.5), "Pit"),
    ("WallWest", (1, 31, 102), (-50.5, 15, 0), "Wall"),
    ("WallEast", (1, 31, 102), (50.5, 15, 0), "Wall"),
    ("WallNorth", (100, 31, 1), (0, 15, -50.5), "Wall"),
    ("WallSouth", (100, 31, 1), (0, 15, 50.5), "Wall"),
    ("Ceiling", (102, 1, 102), (0, 30.5, 0), "Wall"),
]


def room_resources():
    out = []
    for name, size, _, mat in ROOM_BOXES:
        out.append(f'[sub_resource type="BoxMesh" id="Mesh_{name}"]\nmaterial = SubResource("Mat_{mat}")\n'
                   f"size = Vector3({fmt(size[0])}, {fmt(size[1])}, {fmt(size[2])})\n")
    return "\n".join(out)


def room_nodes():
    out = ['[node name="Room" type="GMPBody" parent="."]\nbody_type = 0\ntags = 264\n']
    for name, size, center, _ in ROOM_BOXES:
        out.append(box_node(name, "Room", size, center, f"Mesh_{name}"))
    return "\n".join(out)


def stirrer_nodes():
    out = ['[node name="Stirrer" type="GMPBody" parent="."]\n'
           "transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, -4, 0)\n"
           "body_type = 1\ntags = 514\n"
           'script = ExtResource("stirrer")\nrpm = 6.0\n',
           '[node name="HubShape" type="GMPShape" parent="Stirrer"]\n'
           "transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1.3, 0)\nshape = 3\nradius = 0.5\nheight = 2.5\n",
           '[node name="Hub" type="MeshInstance3D" parent="Stirrer"]\n'
           'transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1.3, 0)\nmesh = SubResource("Mesh_Hub")\n']
    for i in range(4):
        rows = yaw_rows(i)
        center = tuple(rows[r][0] * 3.0 for r in range(3))
        t = transform(rows, (center[0], 0.6, center[2]))
        out.append(f'[node name="Paddle{i}Shape" type="GMPShape" parent="Stirrer"]\ntransform = {t}\n'
                   "size = Vector3(5, 1, 0.3)\n")
        out.append(f'[node name="Paddle{i}" type="MeshInstance3D" parent="Stirrer"]\ntransform = {t}\n'
                   'mesh = SubResource("Mesh_Paddle")\n')
    # Two drains in opposite corners of the pit floor, open toward the middle.
    out.append('[node name="PitDrains" type="Node3D" parent="."]\n')
    return "\n".join(out)


SPOTS = [(-30, -30), (30, -30), (-30, 30), (30, 30), (0, 0), (0, 32), (0, -32)]
OMNIS = [((-22, 10, 0), 30), ((26, 8, 0), 30), ((0, 4, 22), 25)]


def light_nodes():
    out = []
    down = "1, 0, 0, 0, 0, 1, 0, -1, 0"
    for i, (x, z) in enumerate(SPOTS):
        out.append(f'[node name="Spot{i}" type="SpotLight3D" parent="Lights"]\n'
                   f"transform = Transform3D({down}, {x}, 28.5, {z})\n"
                   "light_color = Color(1, 0.93, 0.85, 1)\nlight_energy = 25.0\nshadow_enabled = true\n"
                   "spot_range = 45.0\nspot_angle = 55.0\n")
    for i, ((x, y, z), r) in enumerate(OMNIS):
        out.append(f'[node name="Omni{i}" type="OmniLight3D" parent="Lights"]\n'
                   f"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, {x}, {y}, {z})\n"
                   "light_color = Color(0.75, 0.85, 1, 1)\nlight_energy = 4.0\nshadow_enabled = true\n"
                   f"omni_range = {r}.0\n")
    return "\n".join(out)


STATIC_RESOURCES = """[sub_resource type="Environment" id="Env"]
background_mode = 1
background_color = Color(0.05, 0.055, 0.065, 1)
ambient_light_source = 2
ambient_light_color = Color(0.55, 0.6, 0.7, 1)
ambient_light_energy = 0.35
tonemap_mode = 3
ssao_enabled = true
ssil_enabled = false
ssr_enabled = false
sdfgi_enabled = false
glow_enabled = true
volumetric_fog_enabled = false
fog_enabled = false

[sub_resource type="StandardMaterial3D" id="Mat_Floor"]
albedo_color = Color(0.42, 0.42, 0.4, 1)
roughness = 0.85

[sub_resource type="StandardMaterial3D" id="Mat_Pit"]
albedo_color = Color(0.3, 0.26, 0.2, 1)
roughness = 0.9

[sub_resource type="StandardMaterial3D" id="Mat_Wall"]
albedo_color = Color(0.55, 0.56, 0.58, 1)
roughness = 0.8

[sub_resource type="StandardMaterial3D" id="Mat_Stirrer"]
albedo_color = Color(0.95, 0.65, 0.1, 1)
metallic = 0.7
roughness = 0.4

[sub_resource type="CylinderMesh" id="Mesh_Hub"]
material = SubResource("Mat_Stirrer")
top_radius = 0.5
bottom_radius = 0.5
height = 2.5

[sub_resource type="BoxMesh" id="Mesh_Paddle"]
material = SubResource("Mat_Stirrer")
size = Vector3(5, 1, 0.3)

""" + room_resources() + """
[node name="StressTest" type="Node3D"]

[node name="WorldEnvironment" type="WorldEnvironment" parent="."]
environment = SubResource("Env")

[node name="Spawn" type="Marker3D" parent="." groups=["player_spawn"]]
transform = Transform3D(-1, 0, 0, 0, 1, 0, 0, 0, -1, -20, 1, 6)

[node name="Spawn2" type="Marker3D" parent="." groups=["player_spawn"]]
transform = Transform3D(-1, 0, 0, 0, 1, 0, 0, 0, -1, -24, 1, 6)

[node name="Lights" type="Node3D" parent="."]

"""

STATIC_NODES = "\n".join([room_nodes(), stirrer_nodes(), light_nodes()])


def pit_drains(f):
    # North-west corner, front toward -Z (open side faces the middle); south-east corner, turned half way.
    f.place("PitDrains", "PitDrainNW", "X", (-8, -4, -5), 0, [f"itemsPerSecond = {DELETE_PER_SECOND / 2}"])
    f.place("PitDrains", "PitDrainSE", "X", (7, -4, 4), 2, [f"itemsPerSecond = {DELETE_PER_SECOND / 2}"])


if __name__ == "__main__":
    factory = build()
    pit_drains(factory)
    write(factory)
    print("pieces:", factory.counts)
    print("cells x", factory.lo[0], "..", factory.hi[0], " y", factory.lo[1], "..", factory.hi[1],
          " z", factory.lo[2], "..", factory.hi[2])
    for parent, name, kind, anchor, q, *_ in factory.nodes:
        if kind in ("T", "spawner") or name.endswith("_X") or "Drain" in name:
            print(f"  {name:28s} anchor {anchor} turns {q}")
    b_last = [n for n in factory.nodes if n[1].startswith("B_")][-1]
    print("B ends with", b_last[1], "anchor", b_last[3], "turns", b_last[4])
