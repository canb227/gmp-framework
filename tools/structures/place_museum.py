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
from scenegen import REPO, f, glb_children, ensure_import, script_uid
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
        """Axis-aligned box from world corner lo to hi: a static Box3DBody with a mesh, or (solid=False) mesh only."""
        c = tuple((a + b) / 2 for a, b in zip(lo, hi)); s = tuple(abs(b - a) for a, b in zip(lo, hi))
        name = self.uname(name)
        scale = f"Transform3D({f(s[0])}, 0, 0, 0, {f(s[1])}, 0, 0, 0, {f(s[2])}, 0, 0, 0)"
        if solid:
            self.node(f'[node name="{name}" type="Box3DBody" parent="{parent}"]', "body_type = 0", f"box_size = Vector3({f(s[0])}, {f(s[1])}, {f(s[2])})",
                      f"position = Vector3({f(c[0])}, {f(c[1])}, {f(c[2])})")
            self.node(f'[node name="Mesh" type="MeshInstance3D" parent="{parent}/{name}"]', f"transform = {scale}", f'mesh = SubResource("hall_box_{mat}")')
        else:
            self.node(f'[node name="{name}" type="MeshInstance3D" parent="{parent}"]',
                      f"transform = Transform3D({f(s[0])}, 0, 0, 0, {f(s[1])}, 0, 0, 0, {f(s[2])}, {f(c[0])}, {f(c[1])}, {f(c[2])})",
                      f'mesh = SubResource("hall_box_{mat}")')

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
"""

def main():
    global LAB
    txt = open(MUSEUM, encoding="utf-8").read()
    keep = {m.group(1): (m.group(2), m.group(0)) for m in re.finditer(r'\[ext_resource [^\n]*path="([^"]*)" id="(hall_[^"]*)"\]', txt)}
    h = hall(keep)
    LAB = h
    lab()
    wing()
    floor_tex = h.ext_id("res://game/assets/textures/floor_1/floor_1_diffuseOriginal.png", "Texture2D")
    # remove earlier galleries / halls (new resources go where the old ones were)
    old = re.search(r'\[ext_resource [^\n]*id="(sg|hall)_[^"]*"\]\n', txt)
    txt = txt if not old else txt[:old.start()] + "\0HALL_EXT\n" + txt[old.start():]
    txt = re.sub(r'\[ext_resource [^\n]*id="(sg|hall)_[^"]*"\]\n', "", txt)
    txt = re.sub(r'\[sub_resource [^\n]*id="hall_[^"]*"\]\n(?:[^\[\n][^\n]*\n)*\n?', "", txt)
    for marker in ('\n[node name="StructureGallery"', '\n[node name="StructureHall"', '\n[node name="ConceptLab"', '\n[node name="MaterialsWing"'):
        cut = txt.find(marker)
        if cut >= 0:
            txt = txt[:cut].rstrip("\n") + "\n"
    # hand-placed nodes may still use a hall_ resource the hall no longer needs
    for res, (i, line) in keep.items():
        if res not in h.ext_ids and f'ExtResource("{i}")' in txt:
            h.ext.append(line); h.ext_ids[res] = i
    if "\0HALL_EXT\n" in txt:
        txt = txt.replace("\0HALL_EXT\n", "\n".join(h.ext) + "\n")
    else:
        last = [m.end() for m in re.finditer(r"\[ext_resource [^\n]*\]\n", txt)][-1]
        txt = txt[:last] + "\n".join(h.ext) + "\n" + txt[last:]
    first_node = txt.find("\n[node ")
    first_sub = txt.find("\n[sub_resource ")
    at = first_sub if 0 <= first_sub < first_node else first_node
    txt = txt[:at + 1] + SUBS.format(floor_tex=floor_tex) + "\n" + txt[at + 1:]
    txt = txt.rstrip("\n") + "\n\n" + "\n".join(h.nodes).rstrip("\n") + "\n"
    open(MUSEUM, "w", newline="\n").write(txt)
    print(f"structure hall + concept lab: {h.count} structures placed, {len(h.occupied)} cells occupied, {len(h.nodes)} lines")

if __name__ == "__main__":
    main()
