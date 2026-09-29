"""
Builds the Structure Hall: an extension of ObjectMuseum.tscn east of the original floor (x 40..136, z -60..60)
holding one of every new structure in walled, signed category bays, plus demo production lines showing how they
chain together (visual demos: most machines have no behaviour yet).

    python3 tools/structures/place_museum.py

Re-running replaces the hall (node "StructureHall"; ext/sub resources with the "hall_" id prefix) and removes
the older "StructureGallery". Every structure is placed on the build grid from its scene's cellOffsets and
checked against every other for overlaps.

Grid: cell (i, j, k) spans world x 2i..2i+2, y 2j..2j+2, z 2k..2k+2 (centre 2i+1, 2j+1, 2k+1). Yaw k turns a
structure's front (-Z) counter-clockwise: 0 faces -Z, 1 faces -X, 2 faces +Z, 3 faces +X.
"""
import os, re, sys, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scenegen import REPO, f, glb_children, ensure_import, script_uid, new_uid
sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "items"))
import gen_items

MUSEUM = os.path.join(REPO, "game", "scenes", "levels", "ObjectMuseum.tscn")
S = "game/scenes/structures/"
HALL_I, HALL_K = (20, 67), (-30, 29)            # hall cells (x 40..136, z -60..60)
LAB_I, LAB_K = (-20, 19), (-60, -31)            # concept lab cells (x -40..40, z -120..-60), south of the museum floor
FIELD_CELLS = 2                                  # exhibit projectors' fields, kept short inside their bays

YAW = {0: (1, 0, 0, 0, 1, 0, 0, 0, 1), 1: (0, 0, 1, 0, 1, 0, -1, 0, 0),
       2: (-1, 0, 0, 0, 1, 0, 0, 0, -1), 3: (0, 0, -1, 0, 1, 0, 1, 0, 0)}

def rot(o, k):
    x, y, z = o
    for _ in range(k % 4):
        x, y, z = z, y, -x
    return (x, y, z)

FWD = {k: rot((0, 0, -1), k) for k in range(4)}
RIGHT = {k: rot((1, 0, 0), k) for k in range(4)}

def cells_of(rel):
    txt = open(os.path.join(REPO, rel + ".tscn"), encoding="utf-8").read()
    m = re.search(r"cellOffsets = Array\[Vector3i\]\(\[(.*?)\]\)", txt)
    if not m:
        return [(0, 0, 0)]
    return [tuple(int(x) for x in t) for t in re.findall(r"Vector3i\((-?\d+), (-?\d+), (-?\d+)\)", m.group(1))]

def display_name(rel):
    txt = open(os.path.join(REPO, rel + ".tscn"), encoding="utf-8").read()
    bp = re.search(r'blueprintItemID = "([^"]*)"', txt)
    if bp and bp.group(1):
        for d, _, fs in os.walk(os.path.join(REPO, "game", "definitions")):
            if bp.group(1) + ".tres" in fs:
                name = re.search(r'displayName = "Blueprint: ([^"]+)"', open(os.path.join(d, bp.group(1) + ".tres")).read())
                if name:
                    extra = {"TurnLeft": " (left)", "SlopeDown": " (down)"}
                    return name.group(1) + next((v for k, v in extra.items() if rel.endswith(k)), "")
    base = os.path.basename(rel)
    if base.startswith("Concept"):
        return re.sub(r"(?<=[a-z])(?=[A-Z])", " ", base[len("Concept"):])
    return {"SpawnTube": "Spawn Tube (developer only)", "ItemVoid": "Item Void", "ItemSpawner": "Item Spawner"}.get(base, base)

# ---------------------------------------------------------------------------- scene text
def group_of(parent):
    """Static boxes are batched per exhibit group (the section and its first sub-group), so each MultiMesh stays
    small enough to be culled when off screen."""
    return "/".join(parent.split("/")[:2])

class Hall:
    def __init__(self, keep=None):
        """keep: {res path: (id, ext line)} of hall_ resources already in the museum. They keep their ids, since
        hand-placed nodes outside the hall may use them."""
        self.keep = keep or {}
        self.next_id = max([int(i[5:]) for i, _ in self.keep.values() if i[5:].isdigit()] + [-1]) + 1
        self.bounds = [(HALL_I, HALL_K), (LAB_I, LAB_K)]
        self.ext, self.ext_ids = [], {}
        self.nodes = []
        self.occupied = {}
        self.names = set()
        self.count = 0
        self.batches = {}          # (node path, mat) -> instance transforms (12 floats each) for one MultiMesh

    def batch(self, where, mat, cols, c, size):
        """One box (basis columns cols, centre c, edge lengths size, in `where`'s frame) drawn by the MultiMesh
        of `mat` boxes under node `where`: the museum's architecture costs one draw call per material per group
        instead of a MeshInstance3D node per box."""
        X, Y, Z = cols
        buf = self.batches.setdefault((where, mat), [])
        for r in range(3):
            buf += [X[r] * size[0], Y[r] * size[1], Z[r] * size[2], c[r]]

    def batch_nodes(self):
        """MultiMesh sub_resources and MultiMeshInstance3D nodes for every batch."""
        subs, nodes = [], []
        for n, ((where, mat), buf) in enumerate(sorted(self.batches.items())):
            i = f"hall_mm_{n}"
            subs.append(f'[sub_resource type="MultiMesh" id="{i}"]\ntransform_format = 1\ninstance_count = {len(buf) // 12}\n'
                        f'mesh = SubResource("hall_box_{mat}")\nbuffer = PackedFloat32Array({", ".join(f(v) for v in buf)})\n')
            nodes += [f'[node name="Boxes{mat.capitalize()}" type="MultiMeshInstance3D" parent="{where}"]', f'multimesh = SubResource("{i}")', ""]
        return subs, nodes

    def ext_id(self, res, kind="PackedScene"):
        if res not in self.ext_ids and res in self.keep:
            self.ext_ids[res], line = self.keep[res]
            self.ext.append(line)
        if res not in self.ext_ids:
            i = f"hall_{self.next_id}"
            self.next_id += 1
            uid = None
            if res.endswith(".tscn"):
                m = re.match(r'\[gd_scene[^\]]*uid="([^"]+)"', open(os.path.join(REPO, res[6:])).read())
                uid = m.group(1) if m else None
            elif res.endswith(".cs"):
                uid = script_uid(res)
            elif os.path.exists(os.path.join(REPO, res[6:] + ".import")):
                m = re.search(r'uid="([^"]+)"', open(os.path.join(REPO, res[6:] + ".import")).read())
                uid = m.group(1) if m else None
            self.ext.append(f'[ext_resource type="{kind}"' + (f' uid="{uid}"' if uid else "") + f' path="{res}" id="{i}"]')
            self.ext_ids[res] = i
        return self.ext_ids[res]

    def uname(self, name):
        n = name
        k = 2
        while n in self.names:
            n = f"{name}{k}"; k += 1
        self.names.add(n)
        return n

    def node(self, header, *props):
        self.nodes += [header] + [p for p in props if p] + [""]

    def group(self, name, parent="StructureHall"):
        self.node(f'[node name="{name}" type="Node3D" parent="{parent}"]')
        return f"{parent}/{name}"

    # --- structures
    def place(self, parent, rel, cell, k, name=None, props=(), label=True):
        cells = [tuple(cell[i] + rot(c, k)[i] for i in range(3)) for c in cells_of(rel)]
        for c in cells:
            if c in self.occupied:
                raise ValueError(f"{rel} at {cell}: cell {c} already holds {self.occupied[c]}")
            if not (c[1] >= 0 and any(bi[0] <= c[0] <= bi[1] and bk[0] <= c[2] <= bk[1] for bi, bk in self.bounds)):
                raise ValueError(f"{rel} at {cell}: cell {c} is outside the hall")
            self.occupied[c] = rel
        name = self.uname(name or os.path.basename(rel))
        pos = (2 * cell[0] + 1, 2 * cell[1] + 1, 2 * cell[2] + 1)
        self.node(f'[node name="{name}" parent="{parent}" instance=ExtResource("{self.ext_id("res://" + rel + ".tscn")}")]',
                  f"transform = Transform3D({', '.join(f(v) for v in YAW[k])}, {f(pos[0])}, {f(pos[1])}, {f(pos[2])})", *props)
        self.count += 1
        if "fields/" in rel:
            self.short_field(parent, name, rel)
        if label:
            xs = [2 * c[0] + 1 for c in cells]; zs = [2 * c[2] + 1 for c in cells]; top = max(2 * c[1] + 2 for c in cells)
            self.label(parent, name + "Label", ((min(xs) + max(xs)) / 2, top + 0.7, (min(zs) + max(zs)) / 2), display_name(rel))
        return cells

    def short_field(self, parent, name, rel, cells=FIELD_CELLS):
        cross = 1.9 if "Antigrav" in rel else 1.6
        glb = "res://game/assets/models/machines/fields/" + ("antigrav_projector.glb" if "Antigrav" in rel else "zeropoint_projector.glb")
        self.node(f'[node name="Field" parent="{parent}/{name}/Model" index="{glb_children(glb)["Field"]}"]', f"scale = Vector3(1, 1, {cells})")
        self.node(f'[node name="FieldTrigger" parent="{parent}/{name}"]', f"box_size = Vector3({f(cross)}, {f(cross)}, {2 * cells})",
                  f"position = Vector3(0, 0, {f(-(1 + cells))})")

    def label(self, parent, name, pos, text, size=40, pixel=0.008, billboard=True, yaw=0, color=None, width=None, outline=12):
        """Label3D; width (metres) wraps the text into a left-aligned block."""
        m = YAW[yaw]
        text = text.replace('"', "'").replace("\n", "\\n")
        self.node(f'[node name="{self.uname(name)}" type="Label3D" parent="{parent}"]',
                  f"transform = Transform3D({', '.join(f(v) for v in m)}, {f(pos[0])}, {f(pos[1])}, {f(pos[2])})",
                  "billboard = 1" if billboard else "double_sided = false", f"pixel_size = {f(pixel)}", f"font_size = {size}", f"outline_size = {outline}",
                  f"modulate = {color}" if color else None,
                  *((f"horizontal_alignment = 0", "autowrap_mode = 3", f"width = {f(width / pixel)}") if width else ()),
                  f'text = "{text}"')

    # --- architecture (colliding blocks and decor strips)
    def block(self, parent, name, lo, hi, mat="panel", solid=True):
        """Axis-aligned box from world corner lo to hi: a static Box3DBody (unless solid=False) plus its mesh in
        the group's MultiMesh."""
        c = tuple((a + b) / 2 for a, b in zip(lo, hi)); s = tuple(abs(b - a) for a, b in zip(lo, hi))
        if solid:
            self.node(f'[node name="{self.uname(name)}" type="Box3DBody" parent="{parent}"]', "body_type = 0", f"box_size = Vector3({f(s[0])}, {f(s[1])}, {f(s[2])})",
                      f"position = Vector3({f(c[0])}, {f(c[1])}, {f(c[2])})")
        self.batch(group_of(parent), mat, IDB, c, s)

    def wall(self, parent, name, a, b, y1, t=0.4, y0=0.0, posts=4.0):
        """Facility wall along a line from a to b (world x,z on the floor): white panels, dark cap and skirting,
        dark posts every `posts` metres."""
        (ax, az), (bx, bz) = a, b
        along_x = abs(bx - ax) > abs(bz - az)
        lo = (min(ax, bx) - (0 if along_x else t / 2), y0, min(az, bz) - (t / 2 if along_x else 0))
        hi = (max(ax, bx) + (0 if along_x else t / 2), y1, max(az, bz) + (t / 2 if along_x else 0))
        self.block(parent, name, lo, hi, "panel")
        pad = 0.05
        self.block(parent, name + "Cap", (lo[0] - pad, y1, lo[2] - pad), (hi[0] + pad, y1 + 0.15, hi[2] + pad), "frame", solid=False)
        self.block(parent, name + "Skirt", (lo[0] - pad, y0, lo[2] - pad), (hi[0] + pad, y0 + 0.3, hi[2] + pad), "frame", solid=False)
        length = abs(bx - ax) if along_x else abs(bz - az)
        n = max(1, int(round(length / posts)))
        for i in range(n + 1):
            u = (min(ax, bx) if along_x else min(az, bz)) + length * i / n
            if along_x:
                self.block(parent, name + "Post", (u - 0.1, y0, lo[2] - pad), (u + 0.1, y1, hi[2] + pad), "frame", solid=False)
            else:
                self.block(parent, name + "Post", (lo[0] - pad, y0, u - 0.1), (hi[0] + pad, y1, u + 0.1), "frame", solid=False)

    def stripe(self, parent, name, lo_xz, hi_xz, mat="hazard"):
        """Painted floor band (no collider)."""
        self.block(parent, name, (lo_xz[0], 0.002, lo_xz[1]), (hi_xz[0], 0.012, hi_xz[1]), mat, solid=False)

    def outline(self, parent, name, i0, i1, k0, k1, mat="hazard", w=0.25):
        x0, x1, z0, z1 = 2 * i0 - 0.5, 2 * i1 + 2.5, 2 * k0 - 0.5, 2 * k1 + 2.5
        self.stripe(parent, name + "N", (x0, z1 - w), (x1, z1))
        self.stripe(parent, name + "S", (x0, z0), (x1, z0 + w))
        self.stripe(parent, name + "W", (x0, z0), (x0 + w, z1))
        self.stripe(parent, name + "E", (x1 - w, z0), (x1, z1))

# ---------------------------------------------------------------------------- category bays
def bay(h, name, title, items, yaw, i0, i1, k0, k1, back_wall=None, root="StructureHall/Bays"):
    """Packs exhibits in rows inside cells i0..i1 x k0..k1, fronts facing out of the bay (yaw), one empty cell
    between neighbours and between rows; signs it and outlines it."""
    parent = h.group(name, root)
    fwd = FWD[yaw]
    front_axis = 0 if fwd[0] else 2
    front_sign = fwd[front_axis]
    along_axis = 2 - front_axis
    lo_hi = {0: (i0, i1), 2: (k0, k1)}
    a0, a1 = lo_hi[along_axis]
    f0, f1 = lo_hi[front_axis]
    # centre the (single) row of exhibits in the bay's depth
    def depth_of(it):
        rel, extra = (it if isinstance(it, tuple) else (it, None))
        rc = [rot(c, yaw) for c in cells_of(rel) + (cells_of(S + "ItemVoid") if extra == "over_void" else [])]
        return max(c[front_axis] for c in rc) - min(c[front_axis] for c in rc) + 1
    inset = max(0, ((f1 - f0 + 1) - max(depth_of(it) for it in items)) // 2)
    front = f0 + inset if front_sign < 0 else f1 - inset     # the line the exhibits' fronts sit on
    cursor, row_depth = a0, 0
    for it in items:
        rel, extra = (it if isinstance(it, tuple) else (it, None))
        cells = cells_of(rel)
        rc = [rot(c, yaw) for c in cells]
        if extra == "over_void":                            # spawn tube stacked on the void it drops into
            vc = [rot(c, yaw) for c in cells_of(S + "ItemVoid")]
            rc_all = rc + vc
        else:
            rc_all = rc
        amin = min(c[along_axis] for c in rc_all); amax = max(c[along_axis] for c in rc_all)
        depth = max(c[front_axis] * -front_sign for c in rc_all) - min(c[front_axis] * -front_sign for c in rc_all) + 1
        if cursor + (amax - amin) > a1:
            cursor = a0
            front += -front_sign * (row_depth + 1)
            row_depth = 0
        anchor = [0, 0, 0]
        # the cells nearest the bay's open edge sit on the front line
        anchor[front_axis] = front - max(c[front_axis] * front_sign for c in rc_all) * front_sign
        anchor[along_axis] = cursor - amin
        if extra == "over_void":
            h.place(parent, S + "ItemVoid", tuple(anchor), yaw, label=False)
            h.place(parent, rel, (anchor[0], 1, anchor[2]), yaw)
            # gantry the tube hangs from: a post behind it and an arm over its hood (feed pipes come down it)
            back = tuple(-v for v in FWD[yaw])
            tx, tz = 2 * anchor[0] + 1, 2 * anchor[2] + 1
            px, pz = tx + back[0] * 1.7, tz + back[2] * 1.7
            h.block(parent, "TubeGantryPost", (px - 0.15, 0, pz - 0.15), (px + 0.15, 6.5, pz + 0.15), "frame")
            h.block(parent, "TubeGantryArm", (min(px, tx) - 0.15, 6.0, min(pz, tz) - 0.15), (max(px, tx) + 0.15, 6.3, max(pz, tz) + 0.15), "frame")
            h.block(parent, "TubeGantryBand", (px - 0.17, 1.0, pz - 0.17), (px + 0.17, 1.15, pz + 0.17), "hazard", solid=False)
        else:
            h.place(parent, rel, tuple(anchor), yaw)
        cursor += (amax - amin) + 2
        row_depth = max(row_depth, depth)
        if not (f0 <= front <= f1):
            raise ValueError(f"bay {name} overflows")
    h.outline(parent, name + "Edge", i0, i1, k0, k1)
    # the category sign, high on the bay's back, facing out
    cx, cz = (2 * i0 + 2 * i1 + 2) / 2, (2 * k0 + 2 * k1 + 2) / 2
    back = {0: (cx, 2 * k1 + 1.6), 2: (cx, 2 * k0 + 0.4), 1: (2 * i1 + 1.6, cz), 3: (2 * i0 + 0.4, cz)}[yaw]
    h.label(parent, name + "Sign", (back[0], 6.4, back[1]), title, size=96, pixel=0.02, billboard=False, yaw=(yaw + 2) % 4)

# ---------------------------------------------------------------------------- demo lines (flow toward +X, yaw 3)
C = S + "conveyors/"
CA = S + "conveyors_advanced/"
CM = S + "conveyors_magnetic/"
CH = S + "chutes/"
E = 3                                                     # east-facing yaw

def run(h, parent, rel, start, n, k=E):
    i, j, kk = start
    for s in range(n):
        d = FWD[k]
        h.place(parent, rel, (i + d[0] * s, j, kk + d[2] * s), k, label=False)

SLOW_SPAWNER = ("interval = 4.0",)

def demos(h):
    root = h.group("Demos")
    def lane(name, title, k):
        p = h.group(name, root)
        h.label(p, name + "Title", (2 * 21 + 1, 3.2, 2 * k + 1), title, size=64, pixel=0.012, billboard=True)
        return p

    # A: ore to plates -- spawner, slope up into the smelter's hopper, press, polisher, switchable splitter, voids
    p = lane("SmeltingLine", "Demo: Smelting & Plate Line", 12)
    h.place(p, S + "ItemSpawner", (24, 0, 12), E, props=SLOW_SPAWNER)
    run(h, p, C + "Conveyor", (25, 0, 12), 2)
    h.place(p, C + "ConveyorSlope", (27, 0, 12), E, label=False)
    run(h, p, C + "Conveyor", (29, 1, 12), 1)
    h.place(p, S + "Smelter", (30, 0, 12), E)
    run(h, p, C + "Conveyor", (32, 0, 12), 2)
    h.place(p, S + "processing/PlatePress", (34, 0, 12), E)
    run(h, p, C + "Conveyor", (36, 0, 12), 1)
    h.place(p, S + "processing/Polisher", (37, 0, 12), E)
    h.place(p, C + "ConveyorSplitterSwitch", (39, 0, 12), E)
    run(h, p, C + "Conveyor", (39, 0, 13), 2, k=2)
    h.place(p, S + "ItemVoid", (39, 0, 15), 2, label=False)
    run(h, p, C + "Conveyor", (39, 0, 11), 2, k=0)
    h.place(p, S + "ItemVoid", (39, 0, 9), 0, label=False)
    h.stripe(p, "Walk", (2 * 22, 2 * 11 - 0.3), (2 * 41, 2 * 11 - 0.05))

    # B: rods with sorting -- filter (matches right, beside the grabber arm), rod extruder, loader over a cross belt
    p = lane("RodLine", "Demo: Sorted Rod Line", 2)
    h.place(p, S + "ItemSpawner", (24, 0, 2), E, props=SLOW_SPAWNER)
    run(h, p, CA + "ConveyorAdvanced", (25, 0, 2), 2)
    h.place(p, S + "sorting/FilterBasic", (27, 0, 2), E)
    run(h, p, C + "Conveyor", (27, 0, 3), 2, k=2)
    h.place(p, S + "ItemVoid", (27, 0, 5), 2, label=False)
    h.place(p, S + "sorting/FilterArm", (25, 0, 4), 3)
    run(h, p, CA + "ConveyorAdvanced", (28, 0, 2), 1)
    h.place(p, S + "processing/RodExtruder", (29, 0, 2), E)
    run(h, p, CA + "ConveyorAdvanced", (31, 0, 2), 1)
    h.place(p, CA + "ConveyorAdvancedLoader", (32, 0, 2), E)
    run(h, p, C + "Conveyor", (33, 0, 2), 3, k=2)
    h.place(p, S + "ItemVoid", (33, 0, 5), 2, label=False)

    # C: launch and catch -- raised deck, launch ramp into a 3x3 hopper on pillars, elbow, sealed chutes, void
    p = lane("LaunchLine", "Demo: Launch & Catch", -6)
    h.block(p, "Deck", (2 * 24, 0, 2 * -6), (2 * 29, 2, 2 * -5), "frame")
    h.stripe(p, "DeckEdge", (2 * 24, 2 * -6 - 0.3), (2 * 29, 2 * -6 - 0.05))
    h.place(p, S + "ItemSpawner", (24, 1, -6), E, props=SLOW_SPAWNER)
    run(h, p, C + "Conveyor", (25, 1, -6), 1)
    h.place(p, S + "launchers/LaunchRamp", (26, 1, -6), E)
    h.place(p, CH + "ChuteHopper3x3", (32, 1, -6), E)
    for (x, z) in ((62.3, -13.7), (67.7, -13.7), (62.3, -8.3), (67.7, -8.3)):
        h.block(p, "Pillar", (x - 0.15, 0, z - 0.15), (x + 0.15, 2, z + 0.15), "frame")
    h.place(p, CH + "ChuteVTurn", (32, 0, -6), E, label=False)
    run(h, p, CH + "ChuteHStraight", (33, 0, -6), 2)
    h.place(p, CH + "ChuteHTurnLeft", (35, 0, -6), E, label=False)
    run(h, p, CH + "ChuteHStraight", (35, 0, -7), 1, k=0)
    h.place(p, S + "ItemVoid", (35, 0, -8), 0, label=False)

    # D: cannon tower -- advanced belt into the cannon, which fires at a hopper topping a chute tower
    p = lane("CannonLine", "Demo: Cannon & Chute Tower", -12)
    run(h, p, CA + "ConveyorAdvanced", (38, 0, -12), 2)
    h.place(p, S + "launchers/Cannon", (40, 0, -12), E)
    h.place(p, CH + "ChuteAdvHopperUp", (45, 2, -12), E, label=False)
    h.place(p, CH + "ChuteAdvVStraight", (45, 1, -12), E, label=False)
    h.place(p, CH + "ChuteAdvVTurn", (45, 0, -12), E, label=False)
    run(h, p, CH + "ChuteAdvHStraight", (46, 0, -12), 2)
    h.place(p, S + "ItemVoid", (48, 0, -12), E, label=False)
    h.label(p, "TowerLabel", (91, 7.2, -23), "Powered Chute Tower")

    # E: catapult -- feed belt into the bucket, flung over into a void
    p = lane("CatapultLine", "Demo: Catapult", -17)
    run(h, p, C + "Conveyor", (37, 0, -17), 2)
    h.place(p, S + "launchers/Catapult", (39, 0, -17), E)
    h.place(p, S + "ItemVoid", (47, 0, -17), E, label=False)

    # F: fields -- a side-fed belt through an antigravity field, and a zero point beam along a belt
    p = lane("FieldLine", "Demo: Field Projectors", 7)
    h.place(p, S + "fields/AntigravProjector", (44, 0, 12), E, label=True)
    h.place(p, S + "ItemSpawner", (46, 0, 11), 2, props=SLOW_SPAWNER, label=False)
    run(h, p, C + "Conveyor", (45, 0, 12), 4)
    h.place(p, S + "ItemVoid", (49, 0, 12), E, label=False)
    h.place(p, S + "fields/ZeroPointProjector", (44, 0, 7), E)
    run(h, p, C + "Conveyor", (45, 0, 7), 4)
    h.place(p, S + "ItemVoid", (49, 0, 7), E, label=False)

    # G: magnetic tunnel -- floor, wall and ceiling magnetic belts stacked in a walled, roofed tunnel
    p = lane("MagneticTunnel", "Demo: Magnetic Tunnel", -1)
    run(h, p, CM + "ConveyorMagnetic", (44, 0, -1), 6)
    run(h, p, CM + "ConveyorMagneticWall", (44, 1, -1), 6)
    run(h, p, CM + "ConveyorMagneticCeiling", (44, 2, -1), 6)
    h.wall(p, "TunnelWall", (88, -2.2), (100, -2.2), 6.0, t=0.4)
    h.block(p, "TunnelRoof", (88, 6.0, -2.4), (100, 6.3, 0.2), "frame")
    h.label(p, "TunnelLabel", (94, 7.2, -1), "Floor / Wall / Ceiling magnetic belts")

# ---------------------------------------------------------------------------- the hall
def hall(keep=None):
    h = Hall(keep)
    h.node('[node name="StructureHall" type="Node3D" parent="."]')
    h.block("StructureHall", "HallFloor", (40, -1, -60), (136, 0, 60), "floor")
    arch = h.group("Architecture")
    # perimeter: tall facility walls, a wide entrance from the museum floor on the west side
    h.wall(arch, "WallNorth", (40, 60), (136, 60), 8.0)
    h.wall(arch, "WallSouth", (40, -60), (136, -60), 8.0)
    h.wall(arch, "WallEast", (136, -60), (136, 60), 8.0)
    h.wall(arch, "WallWestN", (40, 16), (40, 60), 8.0)
    h.wall(arch, "WallWestS", (40, -60), (40, -16), 8.0)
    h.block(arch, "EntranceBeam", (39.8, 7.0, -16), (40.2, 8.0, 16), "frame")
    h.label(arch, "HallTitle", (40.6, 9.2, 0), "STRUCTURE HALL", size=128, pixel=0.03, billboard=False, yaw=3)
    h.label(arch, "HallSubtitle", (40.6, 7.8, 0), "new machines by category  -  demo lines in the middle", size=64, pixel=0.02, billboard=False, yaw=3)
    h.label(arch, "HallTitleInside", (39.4, 9.2, 0), "STRUCTURE HALL", size=128, pixel=0.03, billboard=False, yaw=1)
    # bay partitions: low walls down the middle of the empty cell between neighbouring bays
    for x in (81, 119):                                   # north bays: i 21-39 | 41-58 | 60-66
        h.wall(arch, "PartitionN", (x, 42), (x, 60), 4.0, t=0.3)
    h.wall(arch, "PartitionS", (87, -60), (87, -42), 4.0, t=0.3)       # south bays: i 21-42 | 44-66
    for z in (21, -1, -21):                               # east bays: k 11-19 | 0-9 | -10..-2 | -19..-12
        h.wall(arch, "PartitionE", (118, z), (136, z), 4.0, t=0.3)
    h.wall(arch, "PartitionE", (118, 41), (136, 41), 4.0, t=0.3)
    h.wall(arch, "PartitionE", (118, -39), (136, -39), 4.0, t=0.3)
    h.group("Bays")
    # north: conveyor family (fronts face -Z, into the hall)
    bay(h, "Conveyors", "CONVEYORS", [C + "ConveyorSplitter", C + "ConveyorSplitterSwitch", C + "ConveyorLoader",
        CA + "ConveyorAdvanced", CA + "ConveyorAdvancedTurnRight", CA + "ConveyorAdvancedTurnLeft", CA + "ConveyorAdvancedSlope",
        CA + "ConveyorAdvancedSlopeDown", CA + "ConveyorAdvancedLoader"], 0, 21, 39, 21, 29)
    bay(h, "Magnetic", "MAGNETIC CONVEYORS", [CM + "ConveyorMagnetic" + m + t for m in ("", "Wall", "Ceiling") for t in ("", "TurnRight", "TurnLeft")],
        0, 41, 58, 21, 29)
    bay(h, "Launchers", "LAUNCHERS", [S + "launchers/LaunchRamp", S + "launchers/Cannon", S + "launchers/Catapult"], 0, 60, 66, 21, 29)
    # south: chutes (fronts face +Z)
    bay(h, "Chutes", "CHUTES", [CH + "Chute" + n for n in ("HStraight", "HTurnRight", "HTurnLeft", "VStraight", "VTurn", "HopperUp",
        "DropperDown", "Hopper2x2", "Hopper3x3")], 2, 21, 42, -30, -21)
    bay(h, "PoweredChutes", "POWERED CHUTES", [CH + "ChuteAdv" + n for n in ("HStraight", "HTurnRight", "HTurnLeft", "VStraight", "VTurn",
        "HopperUp", "DropperDown", "Hopper2x2", "Hopper3x3")], 2, 44, 66, -30, -21)
    # east: machines (fronts face -X)
    bay(h, "Sorting", "SORTING", [S + "sorting/FilterBasic", S + "sorting/FilterArm"], 1, 59, 66, 11, 19)
    bay(h, "Fields", "FIELDS", [S + "fields/AntigravProjector", S + "fields/ZeroPointProjector"], 1, 59, 66, 0, 9)
    bay(h, "Processing", "PROCESSING", [S + "processing/PlatePress", S + "processing/RodExtruder", S + "processing/Polisher"], 1, 59, 66, -10, -2)
    bay(h, "Utilities", "UTILITIES", [S + "Smelter", (S + "SpawnTube", "over_void")], 1, 59, 66, -19, -12)
    demos(h)
    return h

# ---------------------------------------------------------------------------- the concept lab
CO = S + "concepts/Concept"
TOOLS = (("tool_tether", "Tether Gun"), ("tool_tag_painter", "Tag Painter"), ("tool_blueprint_stamp", "Blueprint Stamp"))

def lab():
    """Proof-of-concept structures and handheld tools, south of the original museum floor, open to it on the north."""
    h = LAB
    h.node('[node name="ConceptLab" type="Node3D" parent="."]')
    h.block("ConceptLab", "LabFloor", (-40, -1, -120), (40, 0, -60), "floor")
    arch = h.group("Architecture", "ConceptLab")
    h.wall(arch, "WallSouth", (-40, -120), (40, -120), 8.0)
    h.wall(arch, "WallWest", (-40, -120), (-40, -60), 8.0)
    h.wall(arch, "WallEast", (40, -120), (40, -60), 8.0)
    # spine between the two rows of bays, walked round at both ends
    h.wall(arch, "Spine", (-30, -92), (30, -92), 8.0)
    h.wall(arch, "PartitionThermal", (0, -120), (0, -106), 4.0, t=0.3)
    h.label(arch, "ThermalTitle", (0, 7.0, -92.4), "THERMAL PROCESSING", size=128, pixel=0.025, billboard=False, yaw=2)
    h.label(arch, "ThermalSubtitle", (0, 5.8, -92.4), "three heaters and three coolers, each a different physics puzzle",
            size=64, pixel=0.018, billboard=False, yaw=2)
    h.stripe(arch, "Threshold", (-40, -60.4), (40, -60))
    for x in (-5, 13):                                    # bays: i -19..-4 | -2..5 | 7..18
        h.wall(arch, "Partition", (x, -92), (x, -78), 4.0, t=0.3)
    h.label(arch, "LabTitle", (0, 10.2, -91.6), "CONCEPT LAB", size=128, pixel=0.03, billboard=False, yaw=0)
    h.label(arch, "LabSubtitle", (0, 8.8, -91.6), "proof-of-concept machines and tools  -  visual reference, rough behaviour only",
            size=64, pixel=0.02, billboard=False, yaw=0)
    h.group("Bays", "ConceptLab")
    root = "ConceptLab/Bays"
    bay(h, "Routing", "TUBES & ROUTING", [CO + n for n in ("TubeStraight", "TubeBend", "TubeJunction", "TubeReceiver",
        "TippingBucket", "TagGate", "BouncePad", "GravityInverter")], 2, -19, -4, -45, -35, root=root)
    bay(h, "Elevators", "ELEVATORS", [CO + n for n in ("CounterweightElevator", "ScrewElevator", "PlatformElevator")],
        2, -2, 5, -45, -35, root=root)
    bay(h, "Thermal", "THERMAL, LAUNCH & ASSEMBLY", [CO + n for n in ("HeatLamp", "CryoVent", "RailGun", "VortexFunnel", "AssemblyChamber")],
        2, 7, 18, -45, -35, root=root)
    bay(h, "Heaters", "HEATERS", [CO + n for n in ("TunnelFurnace", "MagmaBath", "ImpactForge")], 2, -14, -3, -59, -50, root=root)
    bay(h, "Coolers", "COOLERS", [CO + n for n in ("QuenchTank", "SpiralRadiator", "CounterflowExchanger")], 2, 2, 13, -59, -50, root=root)
    # handheld tools: turning slowly on plinths by the entrance
    tools = h.group("Tools", "ConceptLab")
    plinth = h.ext_id("res://game/assets/models/props/display_plinth.glb", "PackedScene")
    spinner = h.ext_id("res://game/scripts/entities/Spinner.cs", "Script")
    for n, (glb, title) in enumerate(TOOLS):
        x, z = -6 + 6 * n, -65.5
        res = f"res://game/assets/models/tools/{glb}.glb"
        ensure_import(res, "res://game/assets/models/shared/salvage_import.gd")
        h.node(f'[node name="{h.uname(title.replace(" ", "") + "Plinth")}" parent="{tools}" instance=ExtResource("{plinth}")]',
               f"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, {f(x)}, 1, {f(z)})")
        turn = h.uname(title.replace(" ", ""))
        h.node(f'[node name="{turn}" type="Node3D" parent="{tools}"]',
               f"transform = Transform3D(2.2, 0, 0, 0, 2.2, 0, 0, 0, 2.2, {f(x)}, 1.35, {f(z)})",
               f'script = ExtResource("{spinner}")', "axis = Vector3(0, 1, 0)", "speed = 0.6")
        h.node(f'[node name="Model" parent="{tools}/{turn}" instance=ExtResource("{h.ext_id(res, "PackedScene")}")]')
        h.node(f'[node name="AnimationPlayer" parent="{tools}/{turn}/Model"]', 'autoplay = "idle-loop"')
        h.label(tools, turn + "Label", (x, 2.6, z), title)
    h.label(tools, "ToolsTitle", (0, 3.6, -65.5), "Handheld tool concepts", size=64, pixel=0.012)

LAB = None

# ---------------------------------------------------------------------------- the materials wing
ORES = {   # the existing ores, shown at the head of their chains (same fields as gen_items.ITEMS)
    "iron_ore": ("iron_ore", "Iron Ore", "base", None, None, None, 1, "mined",
                 "Heavy, rough and round: rolls slowly and settles quickly.", "Smelt into iron ingots; grind for a finer, faster-flowing feed."),
    "copper_ore": ("copper_ore", "Copper Ore", "base", None, None, None, 1, "mined",
                   "Rough, round ore flecked with native copper.", "Smelt into copper ingots, the conductive half of the metal line."),
}
DIFF_COLOR = {1: "Color(0.55, 1, 0.55, 1)", 2: "Color(0.8, 1, 0.45, 1)", 3: "Color(1, 0.85, 0.35, 1)", 4: "Color(1, 0.55, 0.25, 1)", 5: "Color(1, 0.3, 0.3, 1)"}
ROWS = [   # (backdrop wall z, title, item ids west -> east)
    (74, "METALS & SCRAP", ["iron_ore", "iron_ingot", "iron_rod", "iron_plate", "copper_ore", "copper_ingot", "copper_rod",
                            "copper_plate", "scrap_ball", "scrap_ingot"]),
    (94, "BASE RESOURCES  -  HANDLING", ["coal", "coke_briquette", "salt_crystal", "floatstone", "aerogel_tile", "frost_crystal",
                                         "lodestone", "magnet_core"]),
    (116, "BASE RESOURCES  -  HAZARDS", ["quartz_crystal", "glass_pane", "quartz_shards", "latex_resin", "rubber_ball", "sulfur",
                                         "blast_charge", "quicksilver", "voltaic_crystal", "battery_cell"]),
]
SPACING = 6.6

def wing():
    """One station per item: the model turning on a plinth, a walled tray of real, grabbable samples in front and
    a description of its behaviour and intended challenge on the wall behind."""
    h = LAB
    items = {it[0]: it for it in gen_items.ITEMS}
    items.update(ORES)
    h.node('[node name="MaterialsWing" type="Node3D" parent="."]')
    h.block("MaterialsWing", "WingFloor", (-40, -1, 60), (40, 0, 116), "floor")
    arch = h.group("Architecture", "MaterialsWing")
    h.wall(arch, "WallWest", (-40, 60), (-40, 116), 8.0)
    h.wall(arch, "WallEast", (40, 60), (40, 116), 8.0)
    h.stripe(arch, "Threshold", (-40, 60), (40, 60.4))
    h.block(arch, "GateBeamW", (-40, 6.8, 59.8), (-12, 7.6, 60.2), "frame", solid=False)
    h.block(arch, "GateBeamE", (12, 6.8, 59.8), (40, 7.6, 60.2), "frame", solid=False)
    h.label(arch, "WingTitle", (0, 7.2, 59.6), "MATERIALS WING", size=128, pixel=0.03, billboard=False, yaw=2)
    h.label(arch, "WingSubtitle", (0, 5.9, 59.6), "ores, resources and products  -  how each one behaves, and the problem it poses",
            size=64, pixel=0.018, billboard=False, yaw=2)
    spin = h.ext_id("res://game/scripts/entities/Spinner.cs", "Script")
    plinth = h.ext_id("res://game/assets/models/props/display_plinth.glb", "PackedScene")
    for r, (wz, title, ids) in enumerate(ROWS):
        row = h.group(f"Row{r + 1}", "MaterialsWing")
        last = wz == 116
        h.wall(row, "Backdrop", (-40, wz) if last else (-36, wz), (40, wz) if last else (36, wz), 8.0 if last else 5.0)
        h.label(row, "RowTitle", (0, 4.4, wz - 0.25), title, size=96, pixel=0.016, billboard=False, yaw=2)
        x0 = -SPACING * (len(ids) - 1) / 2
        z = wz - 6.0
        prev = None
        for n, iid in enumerate(ids):
            (_, name, group, shape, phys, tags, diff, source, behaviour, challenge) = items[iid]
            x = x0 + n * SPACING
            sname = name.replace(" ", "")
            if iid in ORES:
                glb, scene = f"res://game/assets/models/props/{iid}.glb", f"res://game/scenes/items/world/resources/{iid}.tscn"
            else:
                glb, scene = f"res://game/assets/models/items/{iid}.glb", gen_items.scene_res(iid, group)
            st = h.group(sname, row)
            h.node(f'[node name="Plinth" parent="{st}" instance=ExtResource("{plinth}")]',
                   f"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, {f(x)}, 1, {f(z)})")
            h.node(f'[node name="Turntable" type="Node3D" parent="{st}"]',
                   f"transform = Transform3D(1.3, 0, 0, 0, 1.3, 0, 0, 0, 1.3, {f(x)}, 1.75, {f(z)})",
                   f'script = ExtResource("{spin}")', "axis = Vector3(0, 1, 0)", "speed = 0.5")
            h.node(f'[node name="Model" parent="{st}/Turntable" instance=ExtResource("{h.ext_id(glb, "PackedScene")}")]')
            h.label(st, "Name", (x, 3.1, z), name, size=56, pixel=0.01, color=DIFF_COLOR[diff])
            # samples: a walled tray of real items in front of the plinth
            tz = z - 2.4
            h.block(st, "TrayBase", (x - 1.3, 0, tz - 0.8), (x + 1.3, 0.06, tz + 0.8), "frame")
            for (lo, hi) in (((x - 1.3, tz - 0.8), (x + 1.3, tz - 0.7)), ((x - 1.3, tz + 0.7), (x + 1.3, tz + 0.8)),
                             ((x - 1.3, tz - 0.8), (x - 1.2, tz + 0.8)), ((x + 1.2, tz - 0.8), (x + 1.3, tz + 0.8))):
                h.block(st, "TrayWall", (lo[0], 0.06, lo[1]), (hi[0], 0.45, hi[1]), "panel")
            h.stripe(st, "TrayEdge", (x - 1.3, tz - 0.83), (x + 1.3, tz - 0.8))
            sid = h.ext_id(scene, "PackedScene")
            for k, dx in enumerate((-0.55, 0.55)):
                h.node(f'[node name="Sample{k + 1}" parent="{st}" instance=ExtResource("{sid}")]',
                       f"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, {f(x + dx)}, 0.75, {f(tz)})")
            # the story, on the wall behind
            text = (f"{name.upper()}   difficulty {diff}/5\nfrom: {source}\n\n{behaviour}\n\nCHALLENGE: {challenge}")
            h.block(st, "Panel", (x - SPACING / 2 + 0.2, 0.9, wz - 0.24), (x + SPACING / 2 - 0.2, 3.9, wz - 0.2), "frame", solid=False)
            h.block(st, "PanelBar", (x - SPACING / 2 + 0.2, 3.9, wz - 0.26), (x + SPACING / 2 - 0.2, 3.98, wz - 0.2), "hazard", solid=False)
            h.label(st, "Story", (x, 2.4, wz - 0.3), text, size=26, pixel=0.0052, billboard=False, yaw=2, width=SPACING - 0.8, outline=4)
            if prev and prev[1] in source:                               # this one is made from its left neighbour
                how = source.split("->")[-1].strip()
                h.label(st, "Arrow", (x - SPACING / 2, 1.9, z), f"{how}  >", size=40, pixel=0.008, color="Color(1, 0.8, 0.3, 1)")
                h.stripe(st, "ChainStripe", (x - SPACING + 0.9, z - 0.1), (x - 0.9, z + 0.1))
            prev = (iid, name)

# ---------------------------------------------------------------------------- the puzzle rooms wing
def cross3(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def xf(cols, pos, scale=(1, 1, 1)):
    """Transform3D text from basis columns (X, Y, Z), a position and a per-axis scale (the format is row-major)."""
    X, Y, Z = cols
    rows = [(X[r] * scale[0], Y[r] * scale[1], Z[r] * scale[2]) for r in range(3)]
    return "Transform3D(" + ", ".join(f(v) for row in rows for v in row) + f", {f(pos[0])}, {f(pos[1])}, {f(pos[2])})"
IDB = ((1, 0, 0), (0, 1, 0), (0, 0, 1))
def on_surface(n, fwd):
    """Basis for a structure lying on a surface with inward normal n and flow fwd (model -Z = flow, +Y = n)."""
    return (cross3(fwd, n), n, tuple(-v for v in fwd))

ROOM = (-80.0, 7.5, -30.0)          # rotating room centre = its axis (world X)
RH, RT = 5.0, 0.3                   # inner half size, wall thickness
DOOR = 1.0                          # doorway half width in the end faces
M_MAG = "res://game/assets/models/conveyors_magnetic/conveyor_mag_straight.glb"

def room_path():
    """The tableau: spawner -> floor -> up the far wall -> across the ceiling -> down the near wall -> floor -> deposit.
    Cells (i, j, k) of the 5x5x5 room, surface normal (into the room) and flow direction."""
    up, down, north_wall, south_wall = (0, 1, 0), (0, -1, 0), (0, 0, -1), (0, 0, 1)
    X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)
    nY, nZ = (0, -1, 0), (0, 0, -1)
    return ([((1, 0, k), up, Z) for k in (1, 2, 3, 4)] +
            [((1, j, 4), north_wall, Y) for j in (1, 2, 3)] +
            [((1, 4, 4), down, nZ), ((1, 4, 3), down, nZ), ((1, 4, 2), down, nZ), ((1, 4, 1), down, X), ((2, 4, 1), down, X),
             ((3, 4, 1), down, nZ), ((3, 4, 0), down, nZ)] +
            [((3, j, 0), south_wall, nY) for j in (3, 2, 1)] +
            [((3, 0, k), up, Z) for k in (0, 1, 2, 3)])

ITEMS_ON_BELT = [   # glb, half height along the belt normal, lying rod
    ("res://game/assets/models/items/iron_ingot.glb", 0.15, False), ("res://game/assets/models/items/iron_plate.glb", 0.04, False),
    ("res://game/assets/models/items/scrap_ball.glb", 0.34, False), ("res://game/assets/models/items/iron_rod.glb", 0.1, True),
    ("res://game/assets/models/items/magnet_core.glb", 0.25, False),
]

def puzzle_rooms():
    h = LAB
    h.node('[node name="PuzzleRooms" type="Node3D" parent="."]')
    # the floor, with the stairwell down to the lower level cut out of it
    ox0, ox1, oz0, oz1 = STAIRWELL
    for lo, hi in (((-150, -60), (ox0, 60)), ((ox1, -60), (-40, 60)), ((ox0, -60), (ox1, oz0)), ((ox0, oz1), (ox1, 60))):
        h.block("PuzzleRooms", "WingFloor", (lo[0], -1, lo[1]), (hi[0], 0, hi[1]), "floor")
    arch = h.group("Architecture", "PuzzleRooms")
    h.wall(arch, "WallNorth", (-150, 60), (-40, 60), 10.0)
    h.wall(arch, "WallSouth", (-150, -60), (-40, -60), 10.0)
    h.wall(arch, "WallWest", (-150, -60), (-150, 60), 10.0)
    h.stripe(arch, "Threshold", (-40.4, -60), (-40, 60))
    h.label(arch, "WingTitle", (-40.6, 11.2, 0), "PUZZLE ROOMS", size=128, pixel=0.03, billboard=False, yaw=1)
    h.label(arch, "WingSubtitle", (-40.6, 9.9, 0), "whole-room puzzle concepts  -  walk-in proofs of concept", size=64, pixel=0.02, billboard=False, yaw=1)
    tumbler(h)
    carousel(h)
    scales(h)
    scree_slope(h)
    storm_cage(h)
    tar_pit(h)
    lower_level(h)

def tumbler(h):
    RX, RY, RZ = ROOM
    g = h.group("Tumbler", "PuzzleRooms")
    # ---- the static frame: pylons with bearing collars, entrance bridge + stairs, exit landing + stairs
    O = RH + RT                                           # outer half size
    for sx in (-1, 1):
        px = RX + sx * (O + 1.0)
        if sx < 0:
            h.block(g, "PylonWest", (px - 0.7, 0, RZ - 1.5), (px + 0.7, RY + 1.8, RZ + 1.5), "frame")
        else:
            for (z0, z1) in ((-3.0, -1.8), (1.8, 3.0)):     # two legs: the exit landing and bridge pass between them
                h.block(g, "PylonLeg", (px - 0.4, 0, RZ + z0), (px + 0.4, RY + 2.6, RZ + z1), "frame")
            h.block(g, "PylonLintel", (px - 0.4, RY + 2.6, RZ - 3.0), (px + 0.4, RY + 3.3, RZ + 3.0), "frame")
        cx = RX + sx * (O + 0.25)                          # bearing collar (static) round the axis, clear of the room
        for (y0, y1, z0, z1) in ((1.8, 2.3, -2.3, 2.3), (-2.3, -1.8, -2.3, 2.3), (-1.8, 1.8, 1.8, 2.3), (-1.8, 1.8, -2.3, -1.8)):
            h.block(g, "Collar", (cx - 0.2, RY + y0, RZ + z0), (cx + 0.2, RY + y1, RZ + z1), "hazard", solid=False)
    deck = RY - 1.2
    x0 = RX + O + 0.05
    h.block(g, "Bridge", (x0, deck - 0.3, RZ - 0.8), (RX + 15.0, deck, RZ + 0.8), "frame")
    h.block(g, "BridgeHead", (RX + 13.0, deck - 0.3, RZ - 0.8), (RX + 15.0, deck, RZ + 1.6), "frame")
    for sz in (-1, 1):
        h.block(g, "Rail", (RX + O + 1.6, deck, RZ + sz * 0.8 - 0.04), (RX + 15.0, deck + 1.0, RZ + sz * 0.8 + 0.04), "hazard")
    for k in range(20):                                   # entrance stairs, north from the bridge head
        top = deck - 0.3 * (k + 1)
        h.block(g, "Stair", (RX + 13.0, 0, RZ + 1.6 + 0.6 * k), (RX + 15.0, top, RZ + 1.6 + 0.6 * (k + 1)), "panel")
    land = RY - RH                                        # the floor's height whenever the room is at rest
    h.block(g, "Landing", (x0, 0, RZ - 1.5), (RX + 9.0, land, RZ + 1.5), "panel")
    h.stripe(g, "LandingEdge", (x0, RZ - 1.5), (x0 + 0.3, RZ + 1.5))
    for k in range(8):                                    # exit stairs, east from the landing
        top = land - 0.3 * (k + 1)
        h.block(g, "ExitStair", (RX + 9.0 + 0.6 * k, 0, RZ - 1.5), (RX + 9.0 + 0.6 * (k + 1), top, RZ + 1.5), "panel")
    h.label(g, "Title", (RX + 16.5, 9.0, RZ + 3), "PUZZLE ROOM 1: THE TUMBLER", size=96, pixel=0.018)
    h.label(g, "Story", (RX + 16.5, 5.5, RZ + 3),
            "The whole room turns a quarter turn about its long axis, pauses, and turns again: floor becomes wall becomes ceiling. "
            "Everything built inside turns with it, so only magnetic belts keep their loads, and the path from the spawner to the deposit "
            "box has to work in every orientation (or use the turns on purpose: tip a load from one belt run onto another).\n\n"
            "Walk in from the bridge and drop through the hub. Whenever the room stops, a doorway in the near end sits at floor level "
            "over the exit landing. The structures and items here are a frozen tableau; the room and its colliders really turn.",
            size=28, pixel=0.006, width=9.0, outline=4)
    # ---- the rotating room: a kinematic body with every wall and structure collider as a child shape
    body = h.uname("Room")
    h.node(f'[node name="{body}" type="Box3DBody" parent="{g}"]', "body_type = 1", "shape_type = 1", "sphere_radius = 0.05",
           f"position = Vector3({f(RX)}, {f(RY)}, {f(RZ)})", f'script = ExtResource("{h.ext_id("res://game/scripts/entities/RotatingRoom.cs", "Script")}")',
           "axis = Vector3(1, 0, 0)", "stepDegrees = 90.0", "turnSeconds = 4.0", "holdSeconds = 6.0")
    rb = f"{g}/{body}"
    def shape(name, c, size, cols=IDB):
        h.node(f'[node name="{h.uname(name)}" type="Box3DCollisionShape" parent="{rb}"]', f"box_size = Vector3({f(size[0])}, {f(size[1])}, {f(size[2])})",
               f"transform = {xf(cols, c)}")
    def mesh(name, c, size, mat, cols=IDB):
        h.batch(rb, mat, cols, c, size)                   # a MultiMesh on the room body, so it turns with it
    def slab(name, lo, hi, mat="panel", collide=True):
        c = tuple((a + b) / 2 for a, b in zip(lo, hi)); size = tuple(b - a for a, b in zip(lo, hi))
        if collide: shape(name + "Col", c, size)
        mesh(name, c, size, mat)
    # the four turning faces: floor-plate inside, a window band down the middle, frame edges
    for axis in (1, 2):
        for sgn in (-1, 1):
            lo, hi = [-O, 0, 0], [O, 0, 0]
            lo[axis], hi[axis] = sgn * RH if sgn > 0 else -O, O if sgn > 0 else -RH
            other = 3 - axis
            nm = {1: "Y", 2: "Z"}[axis] + ("P" if sgn > 0 else "N")
            shape(f"Face{nm}Col", tuple((a + b) / 2 for a, b in zip(lo, hi)), tuple((hi[i] - lo[i]) if i != other else 2 * O for i in range(3)))
            for (a0, a1, mat) in ((-O, -1.0, "floor"), (-1.0, 1.0, "glass"), (1.0, O, "floor")):
                l2, h2 = list(lo), list(hi); l2[other], h2[other] = a0, a1
                mesh(f"Face{nm}", tuple((p + q) / 2 for p, q in zip(l2, h2)), tuple(q - p for p, q in zip(l2, h2)), mat)
            for so in (-1, 1):                            # dark edge frames along the long edges
                c = [0, 0, 0]; c[axis] = sgn * (RH + RT / 2); c[other] = so * (O - 0.15)
                size = [2 * O + 0.02, 0, 0]; size[axis] = RT + 0.04; size[other] = 0.32
                mesh(f"Edge{nm}", tuple(c), tuple(size), "frame")
    # end faces: the west one solid with a window, the east one with the entry hub and a doorway at each edge
    slab("EndWest", (-O, -O, -O), (-RH, O, O), "panel")
    cuts = [(-1.8, 1.8, -1.8, 1.8), (-O, -RH + 2.4, -DOOR, DOOR), (RH - 2.4, O, -DOOR, DOOR), (-DOOR, DOOR, -O, -RH + 2.4), (-DOOR, DOOR, RH - 2.4, O)]
    grid = sorted({-O, O, -1.8, 1.8, -DOOR, DOOR, -RH + 2.4, RH - 2.4})
    for a in range(len(grid) - 1):
        for b in range(len(grid) - 1):
            y0, y1, z0, z1 = grid[a], grid[a + 1], grid[b], grid[b + 1]
            cy, cz = (y0 + y1) / 2, (z0 + z1) / 2
            if any(c0 < cy < c1 and d0 < cz < d1 for c0, c1, d0, d1 in cuts):
                continue
            slab("EndEast", (RH, y0, z0), (O, y1, z1), "panel")
    for (y0, y1, z0, z1) in cuts:                         # hazard frames round the hub and the doorways
        w = 0.12
        for (a0, a1, b0, b1) in ((y0 - w, y0, z0 - w, z1 + w), (y1, y1 + w, z0 - w, z1 + w), (y0, y1, z0 - w, z0), (y0, y1, z1, z1 + w)):
            a0, a1, b0, b1 = max(a0, -O), min(a1, O), max(b0, -O), min(b1, O)
            if a1 > a0 and b1 > b0:
                mesh("OpeningFrame", (O + 0.01, (a0 + a1) / 2, (b0 + b1) / 2), (0.02, a1 - a0, b1 - b0), "hazard")
    mesh("AxisMark", (0, 0, 0), (0.12, 0.12, 0.12), "hazard")
    # the tableau: magnetic belts, frozen items, spawner and deposit box
    cell = lambda i, j, k: (2 * i - 4, 2 * j - 4, 2 * k - 4)
    mag = h.ext_id(M_MAG, "PackedScene")
    for n, (c, nrm, fwd) in enumerate(room_path()):
        p = cell(*c)
        cols = on_surface(nrm, fwd)
        h.node(f'[node name="{h.uname("MagBelt")}" parent="{rb}" instance=ExtResource("{mag}")]', f"transform = {xf(cols, p)}")
        shape("BeltCol", tuple(p[i] + nrm[i] * -0.97 for i in range(3)), (1.9, 0.24, 1.9), cols)
        if n % 2 == 0:
            glb, half, lying = ITEMS_ON_BELT[(n // 2) % len(ITEMS_ON_BELT)]
            q = tuple(p[i] + nrm[i] * (-0.85 + half) for i in range(3))
            icol = (cross3(fwd, nrm), fwd, nrm) if lying else cols
            h.node(f'[node name="{h.uname("FrozenItem")}" parent="{rb}" instance=ExtResource("{h.ext_id(glb, "PackedScene")}")]', f"transform = {xf(icol, q)}")
            shape("ItemCol", q, (0.8, 2 * half, 0.8) if not lying else (0.25, 1.2, 0.25), icol)
    up = (0, 1, 0)
    sp = cell(1, 0, 0)
    h.node(f'[node name="Spawner" parent="{rb}" instance=ExtResource("{h.ext_id("res://game/assets/models/props/spawner.glb", "PackedScene")}")]',
           f"transform = {xf(on_surface(up, (0, 0, 1)), sp)}")
    shape("SpawnerCol", (sp[0], sp[1] - 0.2, sp[2]), (1.8, 1.6, 1.8))
    dp = cell(3, 0, 4)
    h.node(f'[node name="Deposit" parent="{rb}" instance=ExtResource("{h.ext_id("res://game/assets/models/props/item_void.glb", "PackedScene")}")]',
           f"transform = {xf(on_surface(up, (0, 0, 1)), dp)}")
    shape("DepositCol", (dp[0], dp[1] - 0.6, dp[2]), (1.9, 0.8, 1.9))
    for name, p, text in (("StartLabel", sp, "SPAWNER"), ("EndLabel", dp, "DEPOSIT")):
        h.node(f'[node name="{name}" type="Label3D" parent="{rb}"]', f"position = Vector3({f(p[0])}, {f(p[1] + 1.4)}, {f(p[2])})",
               "billboard = 1", "pixel_size = 0.01", "font_size = 48", "outline_size = 12", f'text = "{text}"')

# ---------------------------------------------------------------------------- rooms 2-4: shared helpers
def rblock(h, parent, name, c, size, cols, mat="panel", solid=True, props=()):
    """Static box with an arbitrary basis (cols = its X, Y, Z axes), centre c, edge lengths size."""
    if solid:
        h.node(f'[node name="{h.uname(name)}" type="Box3DBody" parent="{parent}"]', "body_type = 0",
               f"box_size = Vector3({f(size[0])}, {f(size[1])}, {f(size[2])})", f"transform = {xf(cols, c)}", *props)
    h.batch(group_of(parent), mat, cols, c, size)

def inst(h, parent, name, res, cols, pos, *props):
    name = h.uname(name)
    h.node(f'[node name="{name}" parent="{parent}" instance=ExtResource("{h.ext_id(res if res.startswith("res://") else "res://" + res + ".tscn", "PackedScene")}")]',
           f"transform = {xf(cols, pos)}", *props)
    return f"{parent}/{name}"

def yaw_cols(deg):
    a = math.radians(deg)
    return ((math.cos(a), 0, -math.sin(a)), (0, 1, 0), (math.sin(a), 0, math.cos(a)))
YAWC = {k: yaw_cols(90 * k) for k in range(4)}

def spawner(h, parent, pos, k, items, interval, out):
    weights = ", ".join(f'"{i}": 1.0' for i in items)
    return inst(h, parent, "ItemSpawner", S + "ItemSpawner", YAWC[k], pos, f"itemWeights = Dictionary[String, float]({{{weights}}})",
                f"interval = {f(interval)}", f"outputPoint = Vector3({f(out[0])}, {f(out[1])}, {f(out[2])})")

def enclosure(h, parent, x0, x1, z0, z1, gap):
    """Low walls round a room plot with an entry gap (z0..z1 of the gap) in the east wall."""
    h.wall(parent, "EncN", (x0, z1), (x1, z1), 2.5, t=0.3)
    h.wall(parent, "EncS", (x0, z0), (x1, z0), 2.5, t=0.3)
    h.wall(parent, "EncW", (x0, z0), (x0, z1), 2.5, t=0.3)
    h.wall(parent, "EncE", (x1, z0), (x1, gap[0]), 2.5, t=0.3)
    h.wall(parent, "EncE", (x1, gap[1]), (x1, z1), 2.5, t=0.3)
    h.stripe(parent, "Entry", (x1 - 0.3, gap[0]), (x1 + 0.3, gap[1]))

def room_signs(h, parent, x, z, title, story, callouts=()):
    h.label(parent, "Title", (x + 1.2, 5.2, z), title, size=96, pixel=0.016, billboard=False, yaw=1)
    h.label(parent, "Story", (x + 1.2, 3.4, z), story, size=28, pixel=0.006, billboard=False, yaw=1, width=11.0, outline=4)
    for pos, text in callouts:
        h.label(parent, "Callout", pos, text, size=40, pixel=0.01, color="Color(1, 0.85, 0.35, 1)")

# ---------------------------------------------------------------------------- room 2: the carousel
CAR = (-80.0, 30.0)
CAR_R, CAR_WALL, CAR_SPIN = 9.0, 11.2, 40.0
CAR_EXITS = (90.0, 270.0)

def ring_pos(a, r, y=0.0):
    t = math.radians(a)
    return (CAR[0] + r * math.cos(t), y, CAR[1] - r * math.sin(t))

def ring_cols(a):
    """Basis whose X points outward at angle a (degrees, counter-clockwise from +X seen from above)."""
    t = math.radians(a)
    X = (math.cos(t), 0, -math.sin(t))
    return (X, (0, 1, 0), (math.sin(t), 0, math.cos(t)))

def carousel(h):
    cx, cz = CAR
    g = h.group("Carousel", "PuzzleRooms")
    enclosure(h, g, cx - 16, cx + 16, cz - 16, cz + 16, (cz - 3, cz + 3))
    # the turntable: a kinematic disc spun by RotatingRoom (continuous), 16 radial planks as its collider
    body = h.uname("Turntable")
    h.node(f'[node name="{body}" type="Box3DBody" parent="{g}"]', "body_type = 1", "shape_type = 1", "sphere_radius = 0.05",
           f"position = Vector3({f(cx)}, 0.6, {f(cz)})", f'script = ExtResource("{h.ext_id("res://game/scripts/entities/RotatingRoom.cs", "Script")}")',
           "axis = Vector3(0, 1, 0)", "continuous = true", f"degreesPerSecond = {f(CAR_SPIN)}")
    tb = f"{g}/{body}"
    h.node(f'[node name="Disc" parent="{tb}" instance=ExtResource("{h.ext_id("res://game/assets/models/machines/rooms/turntable.glb", "PackedScene")}")]')
    for k in range(16):
        a = k / 16 * 360
        c = ring_pos(a, CAR_R / 2, 0.2)
        h.node(f'[node name="{h.uname("Plank")}" type="Box3DCollisionShape" parent="{tb}"]',
               f"box_size = Vector3({f(CAR_R)}, 0.4, {f(2 * CAR_R * math.tan(math.pi / 16))})", "friction = 0.9",
               f"transform = {xf(ring_cols(a), (c[0] - cx, 0.2, c[2] - cz))}")
    h.block(g, "Pedestal", (cx - 0.8, 0, cz - 0.8), (cx + 0.8, 0.55, cz + 0.8), "frame")
    # the rim catcher: skirt, conveyor trough running counter-clockwise, outer wall with two exits and dividers
    rim = h.group("RimCatcher", g)
    n = 24
    for k in range(n):
        a = (k + 0.5) * 360 / n
        t = math.radians(a)
        rblock(h, rim, "Skirt", ring_pos(a, 9.25, 0.33), (0.15, 0.45, 2 * math.pi * 9.25 / n + 0.05), ring_cols(a), "frame")
        tan = (-math.sin(t), 0, -math.cos(t)); out = (math.cos(t), 0, -math.sin(t))
        tv = tuple(1.5 * tan[i] + 0.3 * out[i] for i in range(3))
        rblock(h, rim, "Trough", ring_pos(a, 10.2, 0.05), (1.8, 0.1, 2 * math.pi * 10.2 / n + 0.05), ring_cols(a), "hazard" if k % 2 else "frame",
               props=(f"tangent_velocity = Vector3({f(tv[0])}, {f(tv[1])}, {f(tv[2])})", "friction = 0.8"))
        if all(abs((a - e + 180) % 360 - 180) > 9 for e in CAR_EXITS):
            rblock(h, rim, "RimWall", ring_pos(a, CAR_WALL, 0.6), (0.2, 1.2, 2 * math.pi * CAR_WALL / n + 0.1), ring_cols(a), "panel")
    for e in CAR_EXITS:                                     # dividers just past each exit, so each section drains to its own void
        rblock(h, rim, "Divider", ring_pos(e + 11, 10.25, 0.5), (1.9, 0.8, 0.15), ring_cols(e + 11), "hazard")
    inst(h, rim, "ItemVoid", S + "ItemVoid", YAWC[0], (cx - 1, 1, cz - 12.2))                 # exit at 90 degrees
    inst(h, rim, "ItemVoid", S + "ItemVoid", YAWC[2], (cx + 1, 1, cz + 12.2))                 # exit at 270 degrees
    # the sweep arm, pylon outside the rim at 200 degrees, blade trailing across the disc
    a = 200.0
    t = math.radians(a)
    phi = math.atan2(math.cos(t), -math.sin(t))
    p = ring_pos(a, 12.5, 1.0)
    inst(h, g, "SweepArm", S + "rooms/SweepArm", yaw_cols(math.degrees(phi)), p)
    # spawner on a gantry, dropping pucks and burrs 3 m off the axis
    h.block(g, "GantryBeam", (cx - 13.9, 5.8, cz - 0.5), (cx - 2.1, 6.0, cz + 0.5), "frame")
    h.block(g, "GantryLeg", (cx - 14.4, 0, cz - 0.5), (cx - 13.4, 6.0, cz + 0.5), "frame")
    spawner(h, g, (cx - 3, 7.0, cz), 0, ["slickstone_puck", "burr_seed"], 2.5, (0, -1.6, 0))
    room_signs(h, g, cx + 16, cz + 8, "ROOM 2: THE CAROUSEL",
               "The floor is a 9 m turntable that never stops. Pucks and burrs rain onto it 3 m off the axis. "
               "Slickstone pucks have almost no grip, so they spiral straight off the edge; hooked burrs cling and ride round and round. "
               "Spin alone sorts by friction, but only the burrs can be steered: the sweep arm scrapes them off at one point, while pucks fly off anywhere.\n\n"
               "PUZZLE: the rim catcher conveys everything to two exits. Get pure pucks at one and pure burrs at the other. "
               "Tools of the trade: spin speed, where things land, arm angle and catcher dividers.\n"
               "NEW: slickstone puck, burr seed, sweep arm, rim catcher.",
               callouts=[((cx - 3, 8.8, cz), "Slickstone Puck + Burr Seed"), (ring_pos(200, 12.5, 4.6), "Sweep Arm"),
                         (ring_pos(90, 13.0, 2.4), "Rim Catcher exit A"), (ring_pos(270, 13.0, 2.4), "Rim Catcher exit B")])

# ---------------------------------------------------------------------------- room 3: the scales
SCL = (-125.0, 0.0)

def scales(h):
    cx, cz = SCL
    g = h.group("Scales", "PuzzleRooms")
    enclosure(h, g, cx - 17, cx + 17, cz - 12, cz + 12, (cz - 3, cz + 3))
    # the deck: a dynamic body on a hinge at its centre (Box3DHingeJoint: axis = the joint's local Z = world Z)
    y = 2.0
    body = h.uname("BalanceDeck")
    h.node(f'[node name="{body}" type="Box3DBody" parent="{g}"]', "body_type = 2", "shape_type = 0", "box_size = Vector3(20, 0.4, 8)",
           "density = 0.3", "friction = 0.6", "angular_damping = 1.5", f"position = Vector3({f(cx)}, {f(y)}, {f(cz)})")
    db = f"{g}/{body}"
    h.node(f'[node name="Deck" parent="{db}" instance=ExtResource("{h.ext_id("res://game/assets/models/machines/rooms/balance_floor.glb", "PackedScene")}")]')
    for sz in (-1, 1):
        h.node(f'[node name="{h.uname("Rail")}" type="Box3DCollisionShape" parent="{db}"]', "box_size = Vector3(20, 0.3, 0.2)",
               f"position = Vector3(0, 0.35, {f(sz * 3.9)})")
    h.node(f'[node name="Sled" parent="{db}" instance=ExtResource("{h.ext_id("res://game/assets/models/machines/rooms/counterweight_sled.glb", "PackedScene")}")]',
           "position = Vector3(6, 1.2, 0)")
    h.node(f'[node name="SledShape" type="Box3DCollisionShape" parent="{db}"]', "box_size = Vector3(2, 0.8, 2.6)", "position = Vector3(6, 0.7, 0)")
    lim = math.radians(10)
    h.node(f'[node name="Hinge" type="Box3DHingeJoint" parent="{g}"]', f"position = Vector3({f(cx)}, {f(y)}, {f(cz)})",
           f'body_a = NodePath("../{body}")', "limit_enabled = true", f"lower_limit = {f(-lim)}", f"upper_limit = {f(lim)}")
    h.block(g, "Fulcrum", (cx - 0.6, 0, cz - 3.0), (cx + 0.6, 1.35, cz + 3.0), "frame")
    h.stripe(g, "FulcrumMark", (cx - 0.1, cz - 4.5), (cx + 0.1, cz + 4.5))
    inst(h, g, "TiltGauge", "res://game/assets/models/machines/rooms/tilt_gauge.glb", YAWC[1], (cx, 1.0, cz - 5.6))
    # voids under each open end
    for az in (-1, 3):
        inst(h, g, "ItemVoid", S + "ItemVoid", YAWC[1], (cx - 11.3, 1, az))
    for az in (-3, 1):
        inst(h, g, "ItemVoid", S + "ItemVoid", YAWC[3], (cx + 11.3, 1, az))
    # gantry and spawner over the left third
    for sz in (-1, 1):
        h.block(g, "GantryLeg", (cx - 6.5, 0, cz + sz * 4.6 - 0.3), (cx - 5.5, 6.0, cz + sz * 4.6 + 0.3), "frame")
    h.block(g, "GantryBeam", (cx - 6.5, 5.8, cz - 4.9), (cx - 5.5, 6.0, cz + 4.9), "frame")
    spawner(h, g, (cx - 6, 7.0, cz), 0, ["ballast_shot"], 6.0, (0, -1.6, 0))
    room_signs(h, g, cx + 17, cz + 7, "ROOM 3: THE SCALES",
               "The floor is a 20 m deck balanced on a hinge (it tips up to 10 degrees either way). "
               "The counterweight sled holds it down on the right; every ballast shot that lands on the left tips it back, "
               "and a tipped deck sends the shot rolling off whichever end is low, which tips it again.\n\n"
               "PUZZLE: keep the deck level enough to run a line across it. Your own machines, items in transit and even you weigh on it. "
               "Load both sides together, shift the sled, or build the rolling on purpose into a timer.\n"
               "NEW: ballast shot, counterweight sled, tilt gauge. The deck, sled and hinge really move; the sled isn't motorised yet.",
               callouts=[((cx - 6, 8.8, cz), "Ballast Shot"), ((cx + 6, 3.6, cz), "Counterweight Sled"), ((cx, 3.9, cz - 5.6), "Tilt Gauge")])

# ---------------------------------------------------------------------------- room 4: the scree slope
SCR = (-125.0, -35.0)
SLOPE = math.radians(25)

def scree_slope(h):
    cx, cz = SCR
    g = h.group("ScreeSlope", "PuzzleRooms")
    enclosure(h, g, -143, -112, -46, -27, (-36, -30))
    d = (math.cos(SLOPE), -math.sin(SLOPE), 0); nrm = (math.sin(SLOPE), math.cos(SLOPE), 0)
    cols = (d, nrm, (0, 0, 1))
    top = (-137.0, 8.0)
    L, sieve_len = 13.1, 4.0
    at = lambda s_, off=0.0, z=-35.0: (top[0] + d[0] * s_ + nrm[0] * off, top[1] + d[1] * s_ + nrm[1] * off, z)
    la = L - sieve_len
    rblock(h, g, "Slope", at(la / 2, -0.15), (la, 0.3, 6.0), cols, "floor", props=("friction = 0.5",))
    for side, zc in (("Low", -38.1), ("High", -31.9)):
        span = la if side == "Low" else L
        rblock(h, g, "SlopeWall" + side, at(span / 2, 0.3, zc), (span, 0.9, 0.2), cols, "panel")
    for s_ in (2.0, 6.0, 10.5):                                # supports
        px, py, _ = at(s_, -0.3)
        h.block(g, "Support", (px - 0.3, 0, -37.5), (px + 0.3, py, -36.9), "frame")
        h.block(g, "Support", (px - 0.3, 0, -33.1), (px + 0.3, py, -32.5), "frame")
    h.block(g, "TopDeck", (-140.0, 0, -38.0), (-137.0, 8.0, -32.0), "frame")
    spawner(h, g, (-138.9, 9.0, -35.0), 3, ["scree_pebbles", "shale_slab"], 3.0, (0, -0.3, -2.2))
    # the slot sieve fills the last 4 m of the slope; its hopper empties out of the low (-z) side onto voids
    inst(h, g, "SlotSieve", S + "rooms/SlotSieve", on_surface(nrm, d), at(la + 1.0, 0.2, -37.0))
    inst(h, g, "ItemVoid", S + "ItemVoid", YAWC[0], (-128.0, 1, -39.0))
    # landing at the foot with the terrace catcher (open end toward -z, over a void)
    h.block(g, "Landing", (-125.1, 0, -38.0), (-117.0, 1.6, -31.0), "panel")
    for k in range(5):
        h.block(g, "Step", (-117.0 + 0.6 * k, 0, -35.0), (-117.0 + 0.6 * (k + 1), 1.6 - 0.3 * (k + 1), -32.0), "panel")
    inst(h, g, "TerraceCatcher", S + "rooms/TerraceCatcher", YAWC[1], (-124.1, 2.6, -33.0))
    inst(h, g, "ItemVoid", S + "ItemVoid", YAWC[0], (-124.0, 1, -39.0))
    room_signs(h, g, -112, -40.5, "ROOM 4: THE SCREE SLOPE",
               "A 25 degree scree slope fed with a mix of pebble clumps and flat shale slabs. Pebbles roll and slabs slide, both fast. "
               "At the foot, a slot sieve lets anything under 0.54 m drop through its bars into a side-draining hopper, while slabs skate "
               "over the top into the padded terrace catcher.\n\n"
               "PUZZLE: sort the two, and slow them down without jamming. Shale stops on anything shallower than about 19 degrees, "
               "pebbles never stop, so terraces and slope angles are sorting tools. Every flat step you build collects pebbles until it overflows.\n"
               "NEW: scree pebbles, shale slab, slot sieve, terrace catcher.",
               callouts=[((-138.9, 10.8, -35.0), "Scree Pebbles + Shale Slabs"), ((-126.5, 4.8, -35.0), "Slot Sieve"),
                         ((-124.1, 3.9, -35.0), "Terrace Catcher")])

# ---------------------------------------------------------------------------- rooms 5-6: tag-themed production chains
RM = "res://game/assets/models/machines/rooms/"
IT = "res://game/assets/models/items/"
C = S + "conveyors/"

def frozen(h, parent, glb, pos, cols=IDB, name="Frozen"):
    """A model-only stand-in for an item or machine (no physics): part of a room's tableau."""
    h.node(f'[node name="{h.uname(name)}" parent="{parent}" instance=ExtResource("{h.ext_id(glb, "PackedScene")}")]', f"transform = {xf(cols, pos)}")

def storm_cage(h):
    """Room 5: the electric chain. Storm sand -> (lightning) fulgurite, which leaks charge, repels, grounds and cracks
    -> capacitor press (with copper plate) -> capacitor cell."""
    g = h.group("StormCage", "PuzzleRooms")
    enclosure(h, g, -100, -62, -12, 10, (-3, 1))
    z, y = -1.0, 1.0
    E3 = YAWC[3]                                             # fronts face +X: the line runs west to east
    spawner(h, g, (-95, y, z), 3, ["storm_sand"], 2.0, (0, -0.3, -1.6))
    for x in (-93, -91):
        inst(h, g, "Conveyor", C + "Conveyor", E3, (x, y, z))
    inst(h, g, "StormCollector", S + "rooms/StormCollector", E3, (-89, y, z))
    for x in (-85, -83, -81):
        inst(h, g, "InsulatedBelt", S + "rooms/InsulatedBelt", E3, (x, y, z))
    inst(h, g, "CapacitorPress", S + "rooms/CapacitorPress", E3, (-79, y, z))
    inst(h, g, "InsulatedBelt", S + "rooms/InsulatedBelt", E3, (-77, y, z))
    inst(h, g, "ItemVoid", S + "ItemVoid", E3, (-75, y, -2))
    # tableau: what the line is meant to carry, and what goes wrong
    tab = h.group("Tableau", g)
    belt_top = y - 0.85
    for x, dx in ((-85, -0.5), (-83, 0.3), (-81, -0.2)):     # fulgurite, spaced out on the insulated run (they repel)
        frozen(h, tab, IT + "fulgurite.glb", (x + dx, belt_top + 0.14, z + 0.9), ((0, 0, 1), (1, 0, 0), (0, 1, 0)))
    frozen(h, tab, IT + "capacitor_cell.glb", (-75.5, 0.35, -2.5))
    frozen(h, tab, IT + "capacitor_cell.glb", (-74.6, 0.35, -1.6))
    # copper feed into the press's side port (visual)
    frozen(h, tab, "res://game/assets/models/conveyors/conveyor_straight.glb", (-79, y, -3), YAWC[2], "CopperFeed")
    frozen(h, tab, "res://game/assets/models/conveyors/conveyor_straight.glb", (-79, y, -5), YAWC[2], "CopperFeed")
    frozen(h, tab, IT + "copper_plate.glb", (-79, belt_top + 0.04, -3.3))
    frozen(h, tab, IT + "copper_plate.glb", (-79, belt_top + 0.04, -5.2))
    # a short circuit: a bare steel belt beside the line, where fulgurite grounded out and went flat
    inst(h, g, "Conveyor", C + "Conveyor", E3, (-85, y, 5))
    inst(h, g, "Conveyor", C + "Conveyor", E3, (-83, y, 5))
    frozen(h, tab, IT + "spent_fulgurite.glb", (-85.3, belt_top + 0.14, 5), ((0, 0, 1), (1, 0, 0), (0, 1, 0)))
    frozen(h, tab, IT + "spent_fulgurite.glb", (-83.2, belt_top + 0.14, 5.3), ((0, 0, 1), (1, 0, 0), (0, 1, 0)))
    h.block(tab, "Scorch", (-86, 0.16, 4.3), (-82, 0.17, 5.7), "frame", solid=False)
    # a pile-up: fulgurite in an open hopper, pushing itself apart and cracking
    h.block(tab, "BinFloor", (-94, 0, 4), (-90, 0.1, 7), "frame")
    for (lo, hi) in (((-94, 4), (-90, 4.15)), ((-94, 6.85), (-90, 7)), ((-94, 4), (-93.85, 7)), ((-90.15, 4), (-90, 7))):
        h.block(tab, "BinWall", (lo[0], 0.1, lo[1]), (hi[0], 0.6, hi[1]), "panel")
    for k, (px, pz) in enumerate(((-93.2, 4.8), (-91.0, 6.2), (-92.1, 5.5), (-90.8, 4.9))):
        frozen(h, tab, IT + ("spent_fulgurite.glb" if k % 2 else "quartz_shards.glb"), (px, 0.35, pz))
    # storm atmosphere: lightning rods on the corners of the cage
    for (x, zz) in ((-99, -11), (-99, 9), (-63, -11), (-63, 9)):
        h.block(g, "StormRod", (x - 0.15, 0, zz - 0.15), (x + 0.15, 9.0, zz + 0.15), "frame")
        h.block(g, "StormRodTip", (x - 0.06, 9.0, zz - 0.06), (x + 0.06, 9.8, zz + 0.06), "hazard", solid=False)
    room_signs(h, g, -62, 5.5, "ROOM 5: THE STORM CAGE",
               "ELECTRIC CHAIN (after Fulgora's lightning and Gleba's spoilage).\n"
               "Storm sand rides across the storm collector's strike pad. A bolt lands every 6 s, and only sand on the pad at that "
               "instant fuses into fulgurite. Fulgurite is CHARGED: it repels other charged items (so no piles or hoppers), arcs into bare "
               "metal and goes flat (so it needs insulated belts), cracks if dropped (FRAGILE), and leaks its charge in about a minute (so no buffers). "
               "The capacitor press seals it with a copper plate into a capacitor cell, which is stable, stackable power.\n\n"
               "PUZZLE: time the sand to the strikes, then rush every fulgurite, spaced out and insulated, to the press before it goes flat. "
               "Spent fulgurite can go back under the storm.\n"
               "NEW: storm sand, fulgurite, spent fulgurite, capacitor cell, storm collector, insulated belt, capacitor press.",
               callouts=[((-95, 3.0, z), "Storm Sand"), ((-88, 6.2, z), "Storm Collector (bolt every 6 s)"), ((-83, 2.4, z), "Insulated Belt + Fulgurite"),
                         ((-79, 3.0, z), "Capacitor Press"), ((-75, 2.2, -2), "Capacitor Cells"), ((-84, 2.2, 5), "Short circuit: bare belt"),
                         ((-92, 1.8, 5.5), "Pile-up: repelled and cracked")])

DRUM = (-125.0, 2.5, 37.0)
DRUM_TILT = math.radians(5)

def tar_pit(h):
    """Room 6: the sticky chain. Tar blob (sticky, cures) + chalk -> coating drum -> coated pellet -> press -> bitumen brick."""
    g = h.group("TarPit", "PuzzleRooms")
    enclosure(h, g, -143, -107, 20, 56, (34, 40))
    z = 37.0
    E3 = YAWC[3]
    # raised feed deck: spawner, belts and the belt scraper, 2 m up so the drum can be fed by gravity
    h.block(g, "FeedDeck", (-140.0, 0, 35.6), (-130.0, 2.0, 38.4), "frame")
    spawner(h, g, (-139, 3.0, z), 3, ["tar_blob", "chalk_nodule"], 2.5, (0, -0.3, -1.6))
    for x in (-137, -135, -133):
        inst(h, g, "Conveyor", C + "Conveyor", E3, (x, 3.0, z))
    inst(h, g, "BeltScraper", S + "rooms/BeltScraper", E3, (-131, 3.0, z))
    ch = (math.cos(math.radians(-20)), math.sin(math.radians(-20)), 0)
    rblock(h, g, "FeedChute", (-129.1, 1.75, z), (1.9, 0.06, 1.4), (ch, (-ch[1], ch[0], 0), (0, 0, 1)), "hazard", props=("friction = 0.2",))
    for sz in (-1, 1):
        rblock(h, g, "FeedChuteSide", (-129.1, 2.0, z + sz * 0.72), (1.9, 0.5, 0.05), (ch, (-ch[1], ch[0], 0), (0, 0, 1)), "panel")
    # the coating drum: a kinematic tube (12 planks + 4 lifters) spun about its tilted axis by RotatingRoom
    c5, s5 = math.cos(DRUM_TILT), math.sin(DRUM_TILT)
    tilt = ((c5, -s5, 0), (s5, c5, 0), (0, 0, 1))
    body = h.uname("CoatingDrum")
    h.node(f'[node name="{body}" type="Box3DBody" parent="{g}"]', "body_type = 1", "shape_type = 1", "sphere_radius = 0.05",
           f"transform = {xf(tilt, DRUM)}", f'script = ExtResource("{h.ext_id("res://game/scripts/entities/RotatingRoom.cs", "Script")}")',
           "axis = Vector3(1, 0, 0)", "continuous = true", "degreesPerSecond = 90.0")
    db = f"{g}/{body}"
    h.node(f'[node name="Drum" parent="{db}" instance=ExtResource("{h.ext_id(RM + "coating_drum.glb", "PackedScene")}")]')
    R, n = 1.4, 12
    for k in range(n):
        a = k / n * math.tau
        cols = ((1, 0, 0), (0, math.cos(a), math.sin(a)), (0, -math.sin(a), math.cos(a)))
        h.node(f'[node name="{h.uname("Stave")}" type="Box3DCollisionShape" parent="{db}"]', f"box_size = Vector3(6, 0.15, {f(2 * (R + 0.075) * math.tan(math.pi / n) + 0.02)})",
               "friction = 0.7", f"transform = {xf(cols, (0, (R + 0.075) * math.cos(a), (R + 0.075) * math.sin(a)))}")
    for k in range(4):
        a = k / 4 * math.tau
        cols = ((1, 0, 0), (0, math.cos(a), math.sin(a)), (0, -math.sin(a), math.cos(a)))
        h.node(f'[node name="{h.uname("Lifter")}" type="Box3DCollisionShape" parent="{db}"]', "box_size = Vector3(5.8, 0.26, 0.08)",
               f"transform = {xf(cols, (0, (R - 0.13) * math.cos(a), (R - 0.13) * math.sin(a)))}")
    frozen(h, g, RM + "drum_cradle.glb", DRUM, tilt, "DrumCradle")
    # out of the low end, down a ramp onto the belt to the press (a real Plate Press stands in for the brick press)
    ex = DRUM[0] + 3.0 * c5
    rd = (math.cos(math.radians(-35)), math.sin(math.radians(-35)), 0)
    rblock(h, g, "ExitRamp", (ex + 0.55, 0.55, z), (1.2, 0.06, 1.6), (rd, (-rd[1], rd[0], 0), (0, 0, 1)), "hazard", props=("friction = 0.3",))
    inst(h, g, "Conveyor", C + "Conveyor", E3, (-119, 1.0, z))
    inst(h, g, "PlatePress", S + "processing/PlatePress", E3, (-117, 1.0, z))
    inst(h, g, "Conveyor", C + "Conveyor", E3, (-113, 1.0, z))
    inst(h, g, "ItemVoid", S + "ItemVoid", E3, (-111, 1.0, 36))
    # tableau
    tab = h.group("Tableau", g)
    frozen(h, tab, IT + "coated_pellet.glb", (-119.3, 0.48, z))
    frozen(h, tab, IT + "bitumen_brick.glb", (-112.9, 0.3, z))
    frozen(h, tab, IT + "bitumen_brick.glb", (-111.5, 0.3, 35.2))
    # cold storage: a real cryo vent belt holding chilled blobs (chilled tar stops curing and stops sticking)
    inst(h, g, "ColdStore", S + "concepts/ConceptCryoVent", E3, (-135, 1.0, 45))
    inst(h, g, "Conveyor", C + "Conveyor", E3, (-137, 1.0, 45))
    for x in (-137.4, -135.2):
        frozen(h, tab, IT + "tar_blob.glb", (x, 0.47, 45))
    # a jam: blobs glued into a plug inside a real vertical chute on a stand
    h.block(tab, "JamStand", (-126, 0, 45), (-124, 2, 47), "frame")
    inst(h, g, "JamChute", S + "chutes/ChuteVStraight", YAWC[0], (-125, 3.0, 46))
    for k, (dx, dy) in enumerate(((0, 2.3), (0.2, 2.8), (-0.2, 3.2), (0.1, 3.7))):
        frozen(h, tab, IT + "tar_blob.glb", (-125 + dx, dy, 46 + (0.1 if k % 2 else -0.1)))
    # cured rejects: a bin of tar rock
    h.block(tab, "RejectFloor", (-121, 0, 44), (-117, 0.1, 48), "frame")
    for (lo, hi) in (((-121, 44), (-117, 44.15)), ((-121, 47.85), (-117, 48)), ((-121, 44), (-120.85, 48)), ((-117.15, 44), (-117, 48))):
        h.block(tab, "RejectWall", (lo[0], 0.1, lo[1]), (hi[0], 0.7, hi[1]), "panel")
    for (px, pz) in ((-120.2, 45.0), (-119.3, 46.4), (-118.2, 45.3), (-119.8, 47.2), (-118.0, 47.0)):
        frozen(h, tab, IT + "tar_rock.glb", (px, 0.44, pz))
    # the pit itself: a patch of real, very high-friction floor that bogs down anything crossing it
    h.node(f'[node name="TarPool" type="Box3DBody" parent="{g}"]', "body_type = 0", "box_size = Vector3(8, 0.04, 5)", "friction = 3.0",
           "position = Vector3(-135, 0.02, 27.5)")
    h.batch(group_of(g), "tar", IDB, (-135, 0.02, 27.5), (8, 0.04, 5))
    for (px, pz) in ((-137, 26.5), (-133.5, 28.8), (-135.5, 28.0)):
        frozen(h, tab, IT + "tar_blob.glb", (px, 0.36, pz))
    room_signs(h, g, -107, 44, "ROOM 6: THE TAR PIT",
               "STICKY CHAIN (after Gleba's spoilage clock and Corrundum's clinging chemistry).\n"
               "Tar blobs are STICKY: they grip belts, so they won't drop off a belt end without a scraper; they glue to walls and to each other, "
               "so they clump into plugs in chutes; and they cure into worthless tar rock within a couple of minutes, so buffering them is waste. "
               "Chilling (the cryo vent) pauses both. Tumbling them with chalk in the coating drum rolls them into dry, free-rolling coated pellets, "
               "which a press turns into grippy bitumen bricks.\n\n"
               "PUZZLE: scrape, drop and tumble the blobs with the right chalk ratio before they cure or jam. Cold storage buys time, "
               "and the high-friction tar floor slows anything crossing it.\n"
               "NEW: tar blob, chalk nodule, coated pellet, tar rock, bitumen brick, belt scraper, coating drum.",
               callouts=[((-139, 5.0, z), "Tar Blob + Chalk"), ((-131, 4.8, z), "Belt Scraper"), ((-125, 5.2, z), "Coating Drum (really spins)"),
                         ((-116, 3.2, z), "Press: Bitumen Brick"), ((-136, 2.6, 45), "Cold storage"), ((-125, 5.6, 46), "Jam: glued plug"),
                         ((-119, 2.0, 46), "Cured: tar rock"), ((-135, 1.4, 27.5), "Tar floor (friction 3)")])

# ---------------------------------------------------------------------------- the lower level: ruins
LOW = -25.0                                  # floor top of the lower level
STAIRWELL = (-57.0, -43.0, 44.0, 52.0)       # opening in the Puzzle Rooms floor (x0, x1, z0, z1)
DECOR = "game/scenes/decor/"
RUIN_PROPS = ["CrackedPillar", "CollapsedPillar", "RubblePile", "MossMound", "OvergrownTree", "FernCluster", "HangingVines",
              "CollapsedPanelWall", "BrokenCatwalk", "CableDrapes", "LabBench", "FlickerTerminal", "MonitorBank", "FilingCabinets",
              "CryoPod", "ObservationBooth", "PipeCluster", "HangingLamp", "CeilingFan", "RustedBarrels", "CrateStack", "PuddleDebris",
              "TippedBarriers", "ElevatorRuin"]
KIT_PIECES = ["KitFloor", "KitFloorCracked", "KitWall", "KitWallDamaged", "KitWallWindow", "KitDoorway", "KitHallway", "KitHallwayCorner",
              "KitHallwayBroken", "KitCatwalk", "KitCatwalkCorner", "KitCatwalkStairs", "KitCatwalkSupport", "KitStairs", "KitColumn", "KitRailing"]

def deco(h, parent, name, x, z, yaw=0.0, y=LOW, kit=False):
    return inst(h, parent, name, DECOR + ("kit/" if kit else "") + name, yaw_cols(yaw), (x, y, z))

def spaced(rng, n, x0, x1, z0, z1, gap, taken):
    """n random points in a rectangle, at least `gap` from each other and from `taken` (list of (x, z, r))."""
    out = []
    for _ in range(n * 40):
        if len(out) == n:
            break
        x, z = rng.uniform(x0, x1), rng.uniform(z0, z1)
        if all((x - a) ** 2 + (z - b) ** 2 > (gap + r) ** 2 for a, b, r in taken + [(p[0], p[1], 0) for p in out]):
            out.append((x, z))
    return out

def lower_level(h):
    import random as _r
    rng = _r.Random(2600)
    h.node('[node name="LowerLevel" type="Node3D" parent="."]')
    g = "LowerLevel"
    y = LOW
    # ---- the stairwell: five switchback flights of 24 steps (5 m each) in a walled shaft
    sw = h.group("Stairwell", g)
    ox0, ox1, oz0, oz1 = STAIRWELL
    lanes = ((44.5, 47.5), (48.5, 51.5))
    for k in range(5):
        za, zb = lanes[k % 2]
        for i in range(24):
            top = -5 * k - (i + 1) * 5 / 24
            x0 = ox0 + 0.5 * i if k % 2 == 0 else ox1 - 2 - 0.5 * (i + 1)
            h.block(sw, "Step", (x0, top - 0.25, za), (x0 + 0.5, top, zb), "panel")
        lx = (ox1 - 2, ox1) if k % 2 == 0 else (ox0 - 2, ox0)
        ly = -5 * (k + 1)
        if k < 4:
            h.block(sw, "Landing", (lx[0], ly - 0.3, 44.5), (lx[1], ly, 51.5), "frame")
    for (lo, hi) in (((ox0 - 2.2, 44.3), (ox1, 44.5)), ((ox0 - 2.2, 51.5), (ox1, 51.7)), ((ox0 - 2.2, 44.3), (ox0 - 2.0, 51.7))):
        h.block(sw, "ShaftWall", (lo[0], y, lo[1]), (hi[0], -1, hi[1]), "panel")
    h.block(sw, "ShaftWallE", (ox1, y + 4.5, 44.3), (ox1 + 0.2, -1, 51.7), "panel")          # open at the bottom: the way out
    for (lo, hi) in (((ox0, 43.8), (ox1, 44.0)), ((ox0, 52.0), (ox1, 52.2)), ((ox1, 44.0), (ox1 + 0.2, 52.0)), ((ox0 - 0.2, 47.6), (ox0, 52.0))):
        h.block(sw, "TopRail", (lo[0], 0, lo[1]), (hi[0], 1.1, hi[1]), "hazard")
    h.label(sw, "Down", (ox0 + 7, 3.2, 43.5), "LOWER LEVEL: THE RUINS  (stairs down)", size=64, pixel=0.012)
    # ---- the level: a vast floor under the museum, low walls, a causeway east to the superstructure yard
    h.block(g, "Floor", (-160, y - 1, -140), (170, y, 140), "floor")
    for (a, b) in (((-160, -140), (170, -140)), ((-160, 140), (170, 140)), ((-160, -140), (-160, 140)), ((170, -140), (170, -6)), ((170, 6), (170, 140))):
        h.wall(g, "Edge", a, b, y + 3.0, t=0.4, y0=y)
    h.block(g, "Causeway", (170, y - 1, -6), (205, y, 6), "frame")
    for sz in (-1, 1):
        for k in range(8):
            deco(h, g, "KitRailing", 172 + 4 * k, sz * 5.8, 0, kit=True)
    lights = h.group("Lights", g)
    for lx in range(-140, 171, 45):
        for lz in range(-120, 121, 45):
            h.node(f'[node name="{h.uname("Lamp")}" type="OmniLight3D" parent="{lights}"]', f"position = Vector3({f(lx)}, {f(y + 12)}, {f(lz)})",
                   f"light_color = Color({'1, 0.85, 0.65' if (lx + lz) % 90 else '0.7, 0.9, 1'}, 1)", "light_energy = 2.5", "omni_range = 40.0", "shadow_enabled = false")
    h.label(g, "Title", (-30, y + 9, 50), "THE RUINS", size=160, pixel=0.03, billboard=False, yaw=1)
    h.label(g, "Subtitle", (-30, y + 7, 50), "lower level: decorative props and a level-building kit (non-functional)", size=64, pixel=0.018, billboard=False, yaw=1)
    # ---- catalogue: the kit in one row, the ruin props in two, each labelled
    cat = h.group("Catalogue", g)
    for n, name in enumerate(KIT_PIECES):
        x = -37.5 + n * 5.0
        extra = 4.0 if name in ("KitCatwalk", "KitCatwalkCorner") else 0.0       # decks shown at height, on a support
        deco(h, cat, name, x, 34, 0, y=y + extra, kit=True)
        if extra:
            deco(h, cat, "KitCatwalkSupport", x, 34, 0, y=y + extra, kit=True)
        h.label(cat, name + "Tag", (x, y + 5.6 + extra, 31.5), name.replace("Kit", "Kit: "), size=40, pixel=0.01)
    for n, name in enumerate(RUIN_PROPS):
        row, i = divmod(n, 12)
        x, z = -35.75 + i * 6.5, 60 + row * 22
        deco(h, cat, name, x, z, 0)
        h.label(cat, name + "Tag", (x, y + 7.5, z - 3), re.sub(r"(?<=[a-z])(?=[A-Z])", " ", name), size=44, pixel=0.012)
    # ---- the overgrown laboratory: a kit-built lab (40 x 24 m) gone to seed, reached down a hallway
    lab = h.group("OvergrownLab", g)
    X0, X1, Z0, Z1 = -134.0, -94.0, -60.0, -36.0
    for i in range(10):
        for j in range(6):
            deco(h, lab, "KitFloorCracked" if rng.random() < 0.35 else "KitFloor", X0 + 2 + 4 * i, Z0 + 2 + 4 * j, rng.choice((0, 90, 180, 270)), y=y + 0.3, kit=True)
    for i in range(10):
        for (zz, yaw) in ((Z0, 0), (Z1, 180)):
            kind = "KitWallWindow" if i % 3 == 1 else ("KitWallDamaged" if rng.random() < 0.3 else "KitWall")
            deco(h, lab, kind, X0 + 2 + 4 * i, zz, yaw, y=y + 0.3, kit=True)
    for j in range(6):
        deco(h, lab, "KitWall" if j != 3 else "KitWallDamaged", X0, Z0 + 2 + 4 * j, 90, y=y + 0.3, kit=True)
        deco(h, lab, "KitDoorway" if j == 3 else ("KitWallDamaged" if j == 1 else "KitWall"), X1, Z0 + 2 + 4 * j, 270, y=y + 0.3, kit=True)
    for k in range(5):                                                  # hallway out of the lab's east door
        deco(h, lab, "KitHallwayBroken" if k == 2 else "KitHallway", X1 + 2 + 4 * k, Z0 + 14, 90, y=y + 0.3, kit=True)
    for (px, pz, yaw) in ((-128, -56, 0), (-120, -56, 0), (-112, -56, 0), (-104, -56, 0)):
        deco(h, lab, "LabBench", px, pz + 6, yaw, y=y + 0.3)
        deco(h, lab, "LabBench", px, pz + 12, 180, y=y + 0.3)
    deco(h, lab, "MonitorBank", -114, -38.5, 180, y=y + 0.3)
    for k in range(4):
        deco(h, lab, "CryoPod", -131.5, -56 + 4 * k, 90, y=y + 0.3)
    for k in range(3):
        deco(h, lab, "FlickerTerminal", -106 + 3 * k, -38.8, 180, y=y + 0.3)
    deco(h, lab, "FilingCabinets", -98, -57.5, 0, y=y + 0.3)
    deco(h, lab, "ObservationBooth", -100, -42, 180, y=y + 0.3)
    for (px, pz) in ((-124, -44), (-108, -47)):
        deco(h, lab, "OvergrownTree", px, pz, rng.uniform(0, 360), y=y + 0.3)
    for (px, pz) in ((-126, -40), (-117, -52), (-102, -50), (-110, -41)):
        deco(h, lab, rng.choice(["FernCluster", "MossMound", "PuddleDebris"]), px, pz, rng.uniform(0, 360), y=y + 0.3)
    for (px, pz) in ((-124, -48), (-112, -48), (-100, -48)):
        deco(h, lab, "HangingLamp", px, pz, 0, y=y - 1.7)
    deco(h, lab, "HangingVines", -118, -58, 0, y=y + 0.3)
    deco(h, lab, "ElevatorRuin", -130, -40, 0, y=y + 0.3)
    h.label(lab, "Title", (-114, y + 8, -34), "OVERGROWN LABORATORY", size=96, pixel=0.02)
    # wild growth round the lab
    taken = [(-114, -48, 24)]
    for (px, pz) in spaced(rng, 26, -155, -50, -130, 15, 5.0, taken):
        deco(h, g, rng.choice(["OvergrownTree", "FernCluster", "MossMound", "RubblePile", "CollapsedPillar", "PuddleDebris", "FernCluster", "CrackedPillar"]),
             px, pz, rng.uniform(0, 360))
    # ---- the crumbling factory: a pillar grid with a catwalk loop at 4 m, pipework, cables and wreckage
    fac = h.group("CrumblingFactory", g)
    FX0, FX1, FZ0, FZ1 = 60.0, 156.0, -120.0, 120.0
    for i, px in enumerate(range(int(FX0), int(FX1) + 1, 16)):
        for j, pz in enumerate(range(int(FZ0), int(FZ1) + 1, 16)):
            r = rng.random()
            if r < 0.12:
                deco(h, fac, "CollapsedPillar", px, pz, rng.uniform(0, 360))
            elif r < 0.45:
                deco(h, fac, "CrackedPillar", px, pz, rng.choice((0, 90, 180, 270)))
            else:
                deco(h, fac, "KitColumn", px, pz, 0, kit=True)
    cw_y = y + 4.0                                                      # catwalk loop at 4 m (x 76..140, z -40..40)
    for k in range(15):
        for zz in (-40, 40):
            deco(h, fac, "KitCatwalk", 80 + 4 * k, zz, 90, y=cw_y, kit=True)
    for k in range(19):
        z_ = -36 + 4 * k
        deco(h, fac, "KitCatwalk", 140, z_, 0, y=cw_y, kit=True)
        if z_ == 16:                                                    # a landing on the west run where the stairs come up
            deco(h, fac, "KitFloor", 76, z_, 0, y=cw_y, kit=True)
        else:
            deco(h, fac, "KitCatwalk", 76, z_, 0, y=cw_y, kit=True)
    for (cx, cz, yaw) in ((76, -40, 0), (140, -40, 270), (140, 40, 180), (76, 40, 90)):
        deco(h, fac, "KitCatwalkCorner", cx, cz, yaw, y=cw_y, kit=True)
    for k in range(0, 15, 4):
        for zz in (-40, 40):
            deco(h, fac, "KitCatwalkSupport", 80 + 4 * k, zz, 90, y=cw_y, kit=True)
    for k in range(0, 19, 4):
        for xx in (76, 140):
            deco(h, fac, "KitCatwalkSupport", xx, -36 + 4 * k, 0, y=cw_y, kit=True)
    deco(h, fac, "KitCatwalkStairs", 68, 16, 270, y=y, kit=True)        # two flights up from the floor to the landing
    deco(h, fac, "KitCatwalkStairs", 72, 16, 270, y=y + 2.0, kit=True)
    deco(h, fac, "BrokenCatwalk", 110, 0, 90)
    for (px, pz, yaw) in ((96, -60, 0), (120, -60, 0), (96, 60, 180), (120, 60, 180)):
        deco(h, fac, "PipeCluster", px, pz, yaw)
    for (px, pz) in ((88, -20), (130, 20), (100, 90), (126, -90)):
        deco(h, fac, "CableDrapes", px, pz, rng.choice((0, 90)))
    for (px, pz) in ((84, -96), (140, -70), (104, 104)):
        deco(h, fac, "CeilingFan", px, pz, 0, y=y + 8.0)
    taken = [(110, 0, 10)]
    for (px, pz) in spaced(rng, 30, FX0 - 4, FX1 + 4, FZ0, FZ1, 6.0, taken):
        deco(h, fac, rng.choice(["RustedBarrels", "CrateStack", "RubblePile", "TippedBarriers", "PuddleDebris", "RustedBarrels", "CrateStack", "MossMound"]),
             px, pz, rng.uniform(0, 360))
    h.label(fac, "Title", (108, y + 10, -44), "CRUMBLING FACTORY", size=96, pixel=0.02)
    # ---- the superstructure yard: far out east, in the open
    yard = h.group("SuperstructureYard", g)
    h.block(yard, "YardFloor", (205, y - 1, -180), (560, y, 180), "floor")
    for (name, px, pz, yaw) in (("CoolingTower", 270, -90, 0), ("GantryCrane", 280, 80, 0), ("ReactorSphere", 400, -100, 0),
                                ("ArcologySpire", 420, 70, 0), ("SkyBridge", 500, -10, 90), ("PanelArmWall", 340, 0, 270)):
        deco(h, yard, name, px, pz, yaw)
        h.label(yard, name + "Tag", (px, y + 3, pz + 30 if name != "PanelArmWall" else pz), re.sub(r"(?<=[a-z])(?=[A-Z])", " ", name), size=160, pixel=0.03)
    for (px, pz) in spaced(rng, 40, 215, 550, -170, 170, 12.0, [(270, -90, 25), (280, 80, 30), (400, -100, 25), (420, 70, 14), (500, -10, 45), (340, 0, 22)]):
        deco(h, yard, rng.choice(["RubblePile", "OvergrownTree", "MossMound", "CollapsedPillar", "FernCluster"]), px, pz, rng.uniform(0, 360))
    h.label(yard, "Title", (215, y + 12, 0), "SUPERSTRUCTURE YARD  (proofs of concept)", size=160, pixel=0.03, billboard=False, yaw=1)

SUBS = """[sub_resource type="StandardMaterial3D" id="hall_mat_panel"]
albedo_color = Color(0.78, 0.78, 0.76, 1)
roughness = 0.45

[sub_resource type="StandardMaterial3D" id="hall_mat_frame"]
albedo_color = Color(0.07, 0.075, 0.08, 1)
metallic = 0.6
roughness = 0.45

[sub_resource type="StandardMaterial3D" id="hall_mat_hazard"]
albedo_color = Color(0.85, 0.62, 0.06, 1)
roughness = 0.6

[sub_resource type="StandardMaterial3D" id="hall_mat_floor"]
albedo_color = Color(0.42, 0.43, 0.45, 1)
albedo_texture = ExtResource("{floor_tex}")
uv1_scale = Vector3(0.25, 0.25, 0.25)
uv1_triplanar = true
roughness = 0.7

[sub_resource type="BoxMesh" id="hall_box_panel"]
material = SubResource("hall_mat_panel")

[sub_resource type="BoxMesh" id="hall_box_frame"]
material = SubResource("hall_mat_frame")

[sub_resource type="BoxMesh" id="hall_box_hazard"]
material = SubResource("hall_mat_hazard")

[sub_resource type="BoxMesh" id="hall_box_floor"]
material = SubResource("hall_mat_floor")

[sub_resource type="StandardMaterial3D" id="hall_mat_glass"]
transparency = 1
cull_mode = 2
albedo_color = Color(0.5, 0.85, 1, 0.18)
metallic = 0.3
roughness = 0.05

[sub_resource type="BoxMesh" id="hall_box_glass"]
material = SubResource("hall_mat_glass")

[sub_resource type="StandardMaterial3D" id="hall_mat_tar"]
albedo_color = Color(0.03, 0.025, 0.02, 1)
metallic = 0.2
roughness = 0.08

[sub_resource type="BoxMesh" id="hall_box_tar"]
material = SubResource("hall_mat_tar")
"""

SECTIONS = ("StructureHall", "ConceptLab", "MaterialsWing", "PuzzleRooms", "LowerLevel")
SECTION_DIR = os.path.join(REPO, "game", "scenes", "levels", "museum")

def blocks_of(text):
    """{id: block text} of the [sub_resource] blocks in text."""
    return {m.group(1): m.group(0) for m in re.finditer(r'\[sub_resource [^\n]*id="([^"]+)"\]\n(?:[^\[\n][^\n]*\n)*', text)}

def section_scene(h, name, nodes, subs):
    """One wing as its own scene (game/scenes/levels/museum/<name>.tscn), so no single text scene gets huge and
    each wing can be opened on its own. Paths are made relative to the wing's root."""
    out = []
    for line in nodes:
        if line.startswith("[node "):
            if line == f'[node name="{name}" type="Node3D" parent="."]':
                line = f'[node name="{name}" type="Node3D"]'
            else:
                line = re.sub(r'parent="([^"]+)"', lambda m: 'parent="' + ("." if m.group(1) == name else m.group(1)[len(name) + 1:]) + '"', line)
        out.append(line)
    body = "\n".join(out)
    need, todo = [], re.findall(r'SubResource\("([^"]+)"\)', body)
    while todo:                                                    # sub_resources used, with their own dependencies
        i = todo.pop()
        if i not in need:
            need.append(i); todo += re.findall(r'SubResource\("([^"]+)"\)', subs[i])
    order = list(subs)
    sub_text = "\n".join(subs[i] for i in sorted(need, key=order.index))
    ids = set(re.findall(r'ExtResource\("([^"]+)"\)', body + sub_text))
    ext = [l for l in h.ext if re.search(r'id="([^"]+)"\]$', l).group(1) in ids]
    path = os.path.join(SECTION_DIR, name + ".tscn")
    res = "res://" + os.path.relpath(path, REPO).replace(os.sep, "/")
    old = open(path, encoding="utf-8").read() if os.path.exists(path) else ""
    m = re.match(r'\[gd_scene[^\]]*uid="([^"]+)"', old)
    uid = m.group(1) if m else new_uid(res)
    os.makedirs(SECTION_DIR, exist_ok=True)
    open(path, "w", newline="\n").write(f'[gd_scene format=3 uid="{uid}"]\n\n' + "\n".join(ext) + "\n\n" + sub_text + "\n" + body.rstrip("\n") + "\n")
    return res, uid, os.path.getsize(path)

def main():
    global LAB
    txt = open(MUSEUM, encoding="utf-8").read()
    keep = {}
    for t in [txt] + [open(os.path.join(SECTION_DIR, n + ".tscn"), encoding="utf-8").read() for n in SECTIONS
                      if os.path.exists(os.path.join(SECTION_DIR, n + ".tscn"))]:     # ids stay stable across reruns
        for m in re.finditer(r'\[ext_resource [^\n]*path="([^"]*)" id="(hall_\d+)"\]', t):
            keep.setdefault(m.group(1), (m.group(2), m.group(0)))
    h = hall(keep)
    LAB = h
    lab()
    wing()
    puzzle_rooms()
    floor_tex = h.ext_id("res://game/assets/textures/floor_1/floor_1_diffuseOriginal.png", "Texture2D")
    mm_subs, mm_nodes = h.batch_nodes()
    subs = blocks_of(SUBS.format(floor_tex=floor_tex) + "\n" + "\n".join(mm_subs))
    # split the generated nodes into one scene per wing
    per = {}
    cur = None
    for line in h.nodes + mm_nodes:
        if line.startswith("[node "):
            par = re.search(r'parent="([^"]+)"', line).group(1)
            cur = re.search(r'name="([^"]+)"', line).group(1) if par == "." else par.split("/")[0]
        per.setdefault(cur, []).append(line)
    # remove earlier galleries / halls from the museum (the wings were written into it before they had scenes)
    old = re.search(r'\[ext_resource [^\n]*id="(sg|hall)_[^"]*"\]\n', txt)
    txt = txt if not old else txt[:old.start()] + "\0HALL_EXT\n" + txt[old.start():]
    txt = re.sub(r'\[ext_resource [^\n]*id="(sg|hall)_[^"]*"\]\n', "", txt)
    txt = re.sub(r'\[sub_resource [^\n]*id="hall_[^"]*"\]\n(?:[^\[\n][^\n]*\n)*\n?', "", txt)
    for marker in ('\n[node name="StructureGallery"',) + tuple(f'\n[node name="{s}"' for s in SECTIONS):
        cut = txt.find(marker)
        if cut >= 0:
            txt = txt[:cut].rstrip("\n") + "\n"
    root_ext, root_nodes = [], []
    for name in SECTIONS:
        res, uid, size = section_scene(h, name, per.pop(name), subs)
        i = f"hall_{name}"
        root_ext.append(f'[ext_resource type="PackedScene" uid="{uid}" path="{res}" id="{i}"]')
        root_nodes += [f'[node name="{name}" parent="." instance=ExtResource("{i}")]', ""]
        print(f"{name:14s} {size // 1024:5d} KB  {res}")
    assert not per, f"nodes outside the wings: {list(per)}"
    # hand-placed nodes in the museum may use hall_ resources: keep those lines (and their ids)
    root_ext += [line for i, line in keep.values() if f'ExtResource("{i}")' in txt]
    if "\0HALL_EXT\n" in txt:
        txt = txt.replace("\0HALL_EXT\n", "\n".join(root_ext) + "\n")
    else:
        last = [m.end() for m in re.finditer(r"\[ext_resource [^\n]*\]\n", txt)][-1]
        txt = txt[:last] + "\n".join(root_ext) + "\n" + txt[last:]
    txt = txt.rstrip("\n") + "\n\n" + "\n".join(root_nodes).rstrip("\n") + "\n"
    open(MUSEUM, "w", newline="\n").write(txt)
    print(f"museum: {h.count} structures placed, {len(h.occupied)} cells occupied, {sum(len(b) // 12 for b in h.batches.values())} boxes "
          f"in {len(h.batches)} MultiMeshes, ObjectMuseum.tscn {os.path.getsize(MUSEUM) // 1024} KB")

if __name__ == "__main__":
    main()
