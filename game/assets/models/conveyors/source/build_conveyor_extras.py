"""
Basic (salvage) conveyor additions, exported next to the other basic conveyors (game/assets/models/conveyors):

  conveyor_splitter.glb         1x1x1  fixed wedge splits the flow to the left (-X) and right (+X) side faces
  conveyor_splitter_switch.glb  1x1x1  a centre-pivoted paddle ("Arm", turns about Z) sends everything left or
                                       right; "Lever" (tilts about X) on the front wall is what players flip
  conveyor_loader.glb           1x1x1  straight belt whose front bends up to a lip 0.35 m above belt height, so
                                       items clear the guards of a conveyor running across its front

Same frame as build_conveyors.py: origin at the cell centre, floor z = -1, items enter at the back (y = -1)
moving +Y. Run with tools/blender/run.py build conveyor_extras, or exec() from Blender's text editor.
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix

_HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
         else r"C:\Users\steph\OneDrive\Documents\godot\projects\gmp-framework\game\assets\models\conveyors\source")
_LIB = os.path.normpath(os.path.join(_HERE, "..", "..", "shared", "salvage_lib.py"))
_S = {"__name__": "salvage_lib", "__file__": _LIB}
exec(compile(open(_LIB, encoding="utf-8").read(), _LIB, "exec"), _S)
_SKIP = {"HERE", "OUT_DIR", "PIECES", "build_all", "ICONS", "export", "CONV"}
globals().update({k: v for k, v in _S.items() if not k.startswith("__") and k not in _SKIP})
HERE = _HERE
OUT_DIR = os.path.dirname(HERE)

EXIT_Y = 0.0            # the side exits are open from here to the front face

def half_paths():
    full = PATHS["straight"]
    return full, sub_path(full, 0.0, 1.0 + EXIT_Y), sub_path(full, 1.0 + EXIT_Y, 2.0)

def split_frame(B, rng):
    """Stringers along the whole cell, guards only over the back half; the front half is open both sides."""
    full, back, front = half_paths()
    for side in (-1, 1):
        build_side(B, back, side, rng, panel_len=(0.45, 0.55))
        build_side(B, front, side, rng, guards=False, hubs=False)
        # end post where the guard stops, taped with a hazard band
        x = side * 0.93
        B.box(Vector((x, EXIT_Y - 0.03, BELT_TOP + 0.05)), (0.07, 0.07, 0.42), I3, "steel", C_STEEL, 0.2, rust=0.5)
        B.box(Vector((x, EXIT_Y - 0.03, BELT_TOP + 0.2)), (0.075, 0.075, 0.06), I3, "panel", C_YELLOW, 0.2)
    build_cable(B, full)
    return full

def front_wall(B, hz=0.3):
    """Low end wall across the front face so nothing leaves straight ahead."""
    B.box(Vector((0, 0.955, BELT_TOP + hz / 2 - 0.02)), (1.9, 0.07, hz + 0.04), I3, "metal", C_DARK, 0.2, rust=0.3)
    hazard(B, Vector((-0.84, 0.919, BELT_TOP + 0.03)), (1, 0, 0), (0, 0, 1), 1.68, hz - 0.08, (0, -1, 0), pitch=0.09)
    for sx in (-1, 1):
        B.box(Vector((sx * 0.93, 0.955, BELT_TOP + hz / 2)), (0.07, 0.08, hz + 0.1), I3, "steel", C_STEEL, 0.2, rust=0.5)

# ======================================================================================
def build_splitter():
    rng = random.Random(81); random.seed(81)
    coll = clear_collection("Conveyor_Splitter")
    build_belt(PATHS["straight"], "Belt", coll, 2.0)
    B = Builder()
    split_frame(B, rng)
    front_wall(B, 0.22)
    # wedge: tip toward the incoming flow, faces meeting the side exits at the front corners
    z0, z1 = BELT_TOP + 0.015, BELT_TOP + 0.34
    tip, lf, rf = (0.0, -0.25), (-0.84, 0.9), (0.84, 0.9)
    vprism(B, [tip, rf, lf], z0, z1, "metal", C_FRAME, 0.2, rust=0.3)
    for (a, b, n) in ((tip, lf, -1), (tip, rf, 1)):                      # salvaged white cladding on each face
        pa, pb = Vector((a[0], a[1], 0)), Vector((b[0], b[1], 0))
        d = (pb - pa); ln = d.length; d.normalize()
        nrm = Vector((-d.y, d.x, 0)) * (1 if n < 0 else -1)
        if nrm.y > 0: nrm = -nrm
        R = Matrix((d, ZV, nrm)).transposed()
        mid = (pa + pb) / 2 + nrm * 0.012
        B.box(mid + Vector((0, 0, (z0 + z1) / 2 + 0.02)), (ln - 0.14, 0.2, 0.02), R, "panel", C_WHITE, 0.3)
        B.box(mid + nrm * 0.004 + Vector((0, 0, z0 + 0.035)), (ln - 0.06, 0.05, 0.03), R, "rubber", C_BLACK, 0.1)   # rubber skirt
        for t in (0.25, 0.75):
            p = pa + (pb - pa) * t + nrm * 0.025 + Vector((0, 0, (z0 + z1) / 2 + 0.02))
            B.cyl(p, p + nrm * 0.01, 0.012, 6, "steel", C_STEEL, rust=0.4)
    B.cyl(Vector((0, -0.25, z0)), Vector((0, -0.25, z1 + 0.04)), 0.045, 10, "steel", C_STEEL, 0.2, rust=0.4)       # tip post
    B.box(Vector((0, -0.25, z1 - 0.05)), (0.1, 0.1, 0.07), I3, "panel", C_YELLOW, 0.2)
    vprism(B, [(0.0, -0.08), (0.66, 0.82), (-0.66, 0.82)], z1, z1 + 0.012, "panel", C_WHITE, 0.3)              # lid panel
    vprism(B, [(0.0, -0.22), (0.1, -0.08), (-0.1, -0.08)], z1, z1 + 0.014, "panel", C_YELLOW, 0.2)             # tip marker
    B.box(Vector((0.18, 0.5, z1 + 0.016)), (0.3, 0.22, 0.008), Matrix.Rotation(0.3, 3, 'Z'), "tape", C_TAPE, 0.25)
    status_light(B, PATHS["straight"], 0.5, -1)
    B.to_object("Frame", coll)
    return coll

# ======================================================================================
SWITCH_PIVOT = Vector((0.0, 0.35, BELT_TOP))
SWITCH_ANGLE = math.radians(33.0)       # Arm yaw: +33 deg guides everything right (+X), -33 deg left

def build_splitter_switch():
    rng = random.Random(83); random.seed(83)
    coll = clear_collection("Conveyor_SplitterSwitch")
    build_belt(PATHS["straight"], "Belt", coll, 2.0)
    B = Builder()
    split_frame(B, rng)
    front_wall(B, 0.3)
    # gearbox on the front wall with the handle the player flips; a push rod runs back to the paddle hub
    gb = Vector((0.35, 0.95, BELT_TOP + 0.38))
    B.box(gb, (0.3, 0.1, 0.2), I3, "metal", C_BLUE, 0.2, rust=0.2)
    B.box(gb + Vector((0, -0.055, 0.02)), (0.2, 0.01, 0.1), I3, "panel", C_WHITE, 0.3)
    B.box(gb + Vector((-0.07, -0.062, 0.05)), (0.03, 0.004, 0.02), I3, "glow", C_AMBER, 0.05)
    B.box(gb + Vector((0.06, -0.062, 0.05)), (0.03, 0.004, 0.02), I3, "cyan", C_CYAN, 0.05)
    B.pipe([gb + Vector((-0.15, 0, -0.05)), Vector((-0.3, 0.955, BELT_TOP + 0.2)), Vector((-0.8, 0.97, BELT_TOP + 0.05))], 0.01, 5)
    finish(B, "Frame", coll)
    # the lever: pivot on the gearbox side, handle up
    Lv = Builder()
    Lv.cyl(Vector((-0.03, 0, 0)), Vector((0.03, 0, 0)), 0.035, 10, "metal", C_DARK)
    Lv.cyl(Vector((0, 0, 0)), Vector((0, 0, 0.26)), 0.014, 6, "steel", C_STEEL, rust=0.2)
    Lv.box(Vector((0, 0, 0.29)), (0.06, 0.06, 0.08), I3, "rubber", (0.8, 0.12, 0.08), 0.1)
    node(Lv, "Lever", coll, gb + Vector((0.18, 0, 0.02)))
    # paddle: a salvaged panel on a hub post, 2 m long, centred on the pivot (local +X runs along it)
    A = Builder()
    A.cyl(Vector((0, 0, 0.01)), Vector((0, 0, 0.36)), 0.05, 12, "steel", C_STEEL, 0.2, rust=0.3)
    A.cyl(Vector((0, 0, 0.36)), Vector((0, 0, 0.4)), 0.075, 12, "metal", C_DARK)
    A.box(Vector((0, 0, 0.19)), (1.94, 0.04, 0.3), I3, "metal", C_FRAME, 0.2, rust=0.3)
    for sy in (-1, 1):
        A.box(Vector((0, sy * 0.023, 0.21)), (1.8, 0.01, 0.2), I3, "panel", C_WHITE, 0.3)
        hazard(A, Vector((-0.97, sy * 0.029, 0.04)), (1, 0, 0), (0, 0, 1), 1.94, 0.05, (0, sy, 0), pitch=0.08)
        A.box(Vector((0, sy * 0.028, 0.025)), (1.9, 0.012, 0.03), I3, "rubber", C_BLACK, 0.1)
    for sx in (-1, 1):
        A.cyl(Vector((sx * 0.97, 0, 0.03)), Vector((sx * 0.97, 0, 0.35)), 0.025, 8, "rubber", C_BLACK, 0.1)       # bumpers
    Arm = node(A, "Arm", coll, SWITCH_PIVOT)
    Arm.rotation_euler = (0, 0, SWITCH_ANGLE)
    return coll

# ======================================================================================
def build_loader():
    rng = random.Random(87); random.seed(87)
    coll = clear_collection("Conveyor_Loader")
    path, flat, ls = bend_up_path(**LOADER)
    build_belt(path, "Belt", coll, 2.0)
    B = Builder()
    for side in (-1, 1):
        build_side(B, path, side, rng, panel_len=(0.5, 0.8))
    build_cable(B, path)
    # scaffold legs under the raised front, outside the stringers
    for side in (-1, 1):
        x = side * 0.972
        for s in (path.L - 0.3, flat + 0.35):
            top = path.point(s, x, -0.15)
            B.cyl(Vector((x, top.y, FLOOR + 0.01)), top + Vector((0, 0, 0.02)), 0.024, 8, "steel", C_STEEL, rust=0.55)
            B.box(Vector((x - side * 0.01, top.y, FLOOR + 0.005)), (0.05, 0.12, 0.01), I3, "steel", C_STEEL, rust=0.7)
    # kicker lip at the front edge: a worn steel bar the belt throws items over, hazard-striped underneath
    lip = path.point(path.L, 0, 0)
    B.box(Vector((0, lip.y - 0.03, lip.z - 0.06)), (1.72, 0.05, 0.1), I3, "steel", C_WEAR, 0.15, rust=0.2)
    hazard(B, Vector((-0.86, lip.y - 0.004, lip.z - 0.28)), (1, 0, 0), (0, 0, 1), 1.72, 0.14, (0, 1, 0), pitch=0.1)
    B.box(Vector((0, lip.y - 0.03, lip.z - 0.22)), (1.72, 0.05, 0.22), I3, "metal", C_DARK, 0.2, rust=0.3)
    status_light(B, path, 0.6, 1)
    B.to_object("Frame", coll)
    return coll

PIECES = [
    (build_splitter, "conveyor_splitter.glb"),
    (build_splitter_switch, "conveyor_splitter_switch.glb"),
    (build_loader, "conveyor_loader.glb"),
]
# (collection, icon path under game/assets/icons, card style, view direction) as in render_icons.py
ICONS = [
    ("Conveyor_Splitter", "blueprints/blueprint_conveyor_splitter.png", "blueprint", (1.3, -1.0, 1.4)),
    ("Conveyor_SplitterSwitch", "blueprints/blueprint_conveyor_splitter_switch.png", "blueprint", (1.3, -1.0, 1.4)),
    ("Conveyor_Loader", "blueprints/blueprint_conveyor_loader.png", "blueprint", (1.6, 0.9, 1.0)),
]

def build_all(do_export=True):
    return build_family(PIECES, OUT_DIR, do_export)

if __name__ == "__main__":
    build_all(do_export=False)
