"""
Places every new structure in ObjectMuseum.tscn as a labelled gallery ("StructureGallery"), grid-aligned and
facing the middle of the museum, in the free strips around the existing displays:

  north strip (z 44..60)  conveyor family, fronts facing -Z
  south strip (z -60..-44) chutes, fronts facing +Z
  east strip  (x 26..40)  machines, fronts facing -X

    python3 tools/structures/place_museum.py

Re-running replaces the gallery (its ext_resources use the "sg_" id prefix). Checks the footprints stay in
their strips and don't overlap.
"""
import os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scenegen import REPO, f, glb_children

FIELD_CELLS = 2      # the gallery shortens the projectors' fields so they stay clear of the display alcoves

MUSEUM = os.path.join(REPO, "game", "scenes", "levels", "ObjectMuseum.tscn")
S = "game/scenes/structures/"

NORTH = [S + "conveyors/" + n for n in ("ConveyorSplitter", "ConveyorSplitterSwitch", "ConveyorLoader")] + \
        [S + "conveyors_advanced/" + n for n in ("ConveyorAdvanced", "ConveyorAdvancedTurnRight", "ConveyorAdvancedTurnLeft",
                                                  "ConveyorAdvancedSlope", "ConveyorAdvancedSlopeDown", "ConveyorAdvancedLoader")] + \
        [S + "conveyors_magnetic/ConveyorMagnetic" + m + t for m in ("", "Wall", "Ceiling") for t in ("", "TurnRight", "TurnLeft")]
SOUTH = [S + "chutes/" + p + n for p in ("Chute", "ChuteAdv") for n in
         ("HStraight", "HTurnRight", "HTurnLeft", "VStraight", "VTurn", "HopperUp", "DropperDown", "Hopper2x2")]
EAST = [S + "launchers/" + n for n in ("LaunchRamp", "Cannon", "Catapult")] + \
       [S + "sorting/" + n for n in ("FilterBasic", "FilterArm")] + \
       [S + "fields/" + n for n in ("AntigravProjector", "ZeroPointProjector")] + \
       [S + "processing/" + n for n in ("PlatePress", "RodExtruder", "Polisher")] + \
       [S + "Smelter", ("SPAWN", S + "SpawnTube"), S + "chutes/ChuteHopper3x3", S + "chutes/ChuteAdvHopper3x3"]

def rot(o, k):
    x, y, z = o
    for _ in range(k % 4):
        x, y, z = z, y, -x
    return (x, y, z)

YAW = {0: "1, 0, 0, 0, 1, 0, 0, 0, 1", 1: "0, 0, 1, 0, 1, 0, -1, 0, 0", 2: "-1, 0, 0, 0, 1, 0, 0, 0, -1"}

def cells_of(rel):
    txt = open(os.path.join(REPO, rel + ".tscn"), encoding="utf-8").read()
    m = re.search(r"cellOffsets = Array\[Vector3i\]\(\[(.*?)\]\)", txt)
    if not m:
        return [(0, 0, 0)]
    return [tuple(int(x) for x in t) for t in re.findall(r"Vector3i\((-?\d+), (-?\d+), (-?\d+)\)", m.group(1))]

def label_of(rel):
    txt = open(os.path.join(REPO, rel + ".tscn"), encoding="utf-8").read()
    bp = re.search(r'blueprintItemID = "([^"]*)"', txt)
    if bp and bp.group(1):
        for d, _, fs in os.walk(os.path.join(REPO, "game", "definitions")):
            if bp.group(1) + ".tres" in fs:
                name = re.search(r'displayName = "Blueprint: ([^"]+)"', open(os.path.join(d, bp.group(1) + ".tres")).read())
                if name:
                    extra = {"TurnLeft": " (left)", "SlopeDown": " (down)"}
                    return name.group(1) + next((v for k, v in extra.items() if rel.endswith(k)), "")
    return {"SpawnTube": "Spawn Tube (developer only)", "ItemVoid": "Item Void"}.get(os.path.basename(rel), os.path.basename(rel))

class Strip:
    """A row of structures along `along` (a unit cell vector), fronts on a line, facing `face` (quarter turns)."""
    def __init__(self, name, k, front_axis, front_cell, front_sign, along_axis, start, end):
        self.name, self.k = name, k
        self.front_axis, self.front_cell, self.front_sign = front_axis, front_cell, front_sign
        self.along_axis, self.cursor, self.end = along_axis, start, end
        self.placed = []

    def place(self, rel, cells):
        rc = [rot(c, self.k) for c in cells]
        fa, aa = self.front_axis, self.along_axis
        # front: the cells nearest the museum middle sit on front_cell
        front_extreme = min(c[fa] * self.front_sign for c in rc)
        a_min = min(c[aa] for c in rc); a_max = max(c[aa] for c in rc)
        anchor = [0, 0, 0]
        anchor[fa] = self.front_cell - front_extreme * self.front_sign
        anchor[aa] = self.cursor - a_min
        self.cursor += (a_max - a_min) + 2                    # one empty cell between exhibits
        if self.cursor - 2 > self.end:
            raise ValueError(f"{self.name} strip is full at {rel}")
        world = [tuple(anchor[i] + c[i] for i in range(3)) for c in rc]
        self.placed.append((rel, tuple(anchor), world))
        return tuple(anchor), world

def main():
    # cell index c spans world 2c..2c+2; north: cells z 22..29 (fronts at z index 22); south: z -30..-23; east: x 13..19
    north = Strip("north", 0, 2, 22, 1, 0, -19, 19)
    south = Strip("south", 2, 2, -23, -1, 0, -19, 19)
    east = Strip("east", 1, 0, 13, 1, 2, -21, 21)
    items = []
    for strip, rels in ((north, NORTH), (south, SOUTH), (east, EAST)):
        for rel in rels:
            spawn = isinstance(rel, tuple)
            rel = rel[1] if spawn else rel
            cells = cells_of(rel)
            if spawn:   # the spawn tube and the void it feeds: void in front of the tube, sharing one exhibit
                void = [(x, y, z - 1) for (x, y, z) in cells_of(S + "ItemVoid")]
                anchor, world = strip.place(rel, cells + [(x, 0, z) for (x, _, z) in void])
                items.append((rel, anchor, strip.k, world))
                items.append((S + "ItemVoid", tuple(a + b for a, b in zip(anchor, rot((0, 0, -1), strip.k))), strip.k, []))
            else:
                anchor, world = strip.place(rel, cells)
                items.append((rel, anchor, strip.k, world))
    occupied = {}
    for rel, anchor, k, world in items:
        for c in world:
            if c in occupied and occupied[c] != rel:
                raise ValueError(f"overlap at {c}: {rel} / {occupied[c]}")
            occupied[c] = rel
            if not (-20 <= c[0] <= 19 and -30 <= c[2] <= 29):
                raise ValueError(f"{rel} leaves the floor at {c}")
    txt = open(MUSEUM, encoding="utf-8").read()
    txt = re.sub(r'\[ext_resource [^\n]*id="sg_[^"]*"\]\n', "", txt)
    cut = txt.find('\n[node name="StructureGallery"')
    if cut >= 0:
        txt = txt[:cut].rstrip("\n") + "\n"
    ids, ext = {}, []
    for rel, *_ in items:
        if rel not in ids:
            ids[rel] = f"sg_{len(ids)}"
            uid = re.match(r'\[gd_scene[^\]]*uid="([^"]+)"', open(os.path.join(REPO, rel + ".tscn")).read())
            ext.append(f'[ext_resource type="PackedScene"' + (f' uid="{uid.group(1)}"' if uid else "") +
                       f' path="res://{rel}.tscn" id="{ids[rel]}"]')
    last = [m.end() for m in re.finditer(r"\[ext_resource [^\n]*\]\n", txt)][-1]
    txt = txt[:last] + "\n".join(ext) + "\n" + txt[last:]
    out = ['', '[node name="StructureGallery" type="Node3D" parent="."]', '']
    for grp in ("North", "South", "East"):
        out += [f'[node name="{grp}" type="Node3D" parent="StructureGallery"]', '']
    group = {0: "North", 2: "South", 1: "East"}
    used = set()
    for rel, anchor, k, world in items:
        name = os.path.basename(rel)
        while name in used: name += "_"
        used.add(name)
        pos = (2 * anchor[0] + 1, 2 * anchor[1] + 1, 2 * anchor[2] + 1)
        parent = f"StructureGallery/{group[k]}"
        out += [f'[node name="{name}" parent="{parent}" instance=ExtResource("{ids[rel]}")]',
                f"transform = Transform3D({YAW[k]}, {f(pos[0])}, {f(pos[1])}, {f(pos[2])})", '']
        if world:
            # a floating name tag over the exhibit's front
            fr = {0: (0, -1), 2: (0, 1), 1: (-1, 0)}[k]
            xs = [2 * c[0] + 1 for c in world]; zs = [2 * c[2] + 1 for c in world]; top = max(2 * c[1] + 2 for c in world)
            lx = (min(xs) + max(xs)) / 2 if fr[0] == 0 else (min(xs) - 1.2)
            lz = (min(zs) + max(zs)) / 2 if fr[1] == 0 else ((min(zs) - 1.2) if fr[1] < 0 else (max(zs) + 1.2))
            out += [f'[node name="{name}Label" type="Label3D" parent="{parent}"]',
                    f"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, {f(lx)}, {f(top + 0.6)}, {f(lz)})",
                    "billboard = 1", "pixel_size = 0.008", "font_size = 48", "outline_size = 10",
                    f'text = "{label_of(rel)}"', '']
        if "fields/" in rel:
            cross = 1.9 if "Antigrav" in rel else 1.6
            glb = "res://game/assets/models/machines/fields/" + ("antigrav_projector.glb" if "Antigrav" in rel else "zeropoint_projector.glb")
            out += [f'[node name="Field" parent="{parent}/{name}/Model" index="{glb_children(glb)["Field"]}"]',
                    f"scale = Vector3(1, 1, {FIELD_CELLS})", '',
                    f'[node name="FieldTrigger" parent="{parent}/{name}"]',
                    f"box_size = Vector3({f(cross)}, {f(cross)}, {2 * FIELD_CELLS})",
                    f"position = Vector3(0, 0, {f(-(1 + FIELD_CELLS))})", '']
    txt = txt.rstrip("\n") + "\n" + "\n".join(out).rstrip("\n") + "\n"
    open(MUSEUM, "w", newline="\n").write(txt)
    for s in (north, south, east):
        print(f"{s.name}: {len(s.placed)} exhibits, cursor {s.cursor} / {s.end}")

if __name__ == "__main__":
    main()
