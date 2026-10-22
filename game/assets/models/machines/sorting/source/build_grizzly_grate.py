"""
Grizzly grate blockout. Blender frame: front = +Y, z up, origin at the anchor cell's left-bottom-back corner.
Run in Blender (MCP or the Text Editor) or from the command line with --background --python: it empties the open
file, then builds, saves and exports.

  grizzly_grate.glb  2x4x3 frame (x 0..2, y 0..4, z 0..3). Items come in over the back (from an up-slope conveyor,
                     whose belt top is at z 2.15) onto a grate (y 0..3) that slopes 12 degrees down toward the front.
                     The grate's bars aren't modelled: GrateBars makes them in Godot from its settings. Oversize rides
                     off its end onto a chute (y 3..4) that brings it down to belt height at the front face; fines drop
                     onto a belt tray (top at z 0.5, running +X) that throws them out of the open right side, over the
                     wall of a belt laid alongside.

"Frame" is everything static in the frame's colours. "Belt" is the tray's top surface on its own (material
M_ConvBelt), with v in metres along +X, so Godot can give it the scrolling conveyor belt material.
"""
import bpy, bmesh, math, os
from mathutils import Vector

HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
        else r"C:\Users\steph\OneDrive\Documents\godot\projects\gmp-framework\game\assets\models\machines\sorting\source")

COLORS = {
    "M_Frame": (0.30, 0.31, 0.33),
    "M_Panel": (0.36, 0.40, 0.40),
    "M_Trim": (0.85, 0.58, 0.08),
    "M_ConvBelt": (0.06, 0.06, 0.06),
}
MATERIALS = list(COLORS)

# The grate's top surface: GrateBars sits at (1, GRATE_BACK_Z) on the back face, tilted GRATE_ANGLE down toward +Y.
GRATE_BACK_Z = 2.05
GRATE_ANGLE = math.radians(12)
LENGTH = 4.0           # the whole frame, back to front
GRATE = 3.0            # how far the grate reaches (seen from above); the chute takes the rest
WALL = 0.06            # wall plate thickness
UNDER_GRATE = 0.15     # walls below the grate stop this far under its top, leaving room for thicker bars
OVER_GRATE = 0.6       # side walls reach this far above it
TRIM = 0.04            # the yellow strip along the side walls' tops
TRAY_TOP = 0.5


def grate_top(y):
    return GRATE_BACK_Z - y * math.tan(GRATE_ANGLE)


# The oversize chute: from just under the bars' ends down to a little above a belt's top (0.15) at the front face.
CHUTE_TOP = grate_top(GRATE) - 0.11
CHUTE_BOTTOM = 0.22


class Mesh:
    """A bmesh being built from boxes and prisms, each on one material."""

    def __init__(self):
        self.bm = bmesh.new()

    def box(self, lo, hi, mat):
        """An axis-aligned box from corner lo to corner hi."""
        self.prism([(lo[1], lo[2]), (hi[1], lo[2]), (hi[1], hi[2]), (lo[1], hi[2])], lo[0], hi[0], mat)

    def prism(self, profile, x0, x1, mat):
        """A convex (y, z) profile, counter-clockwise seen from +X, pushed out from x0 to x1."""
        left = [self.bm.verts.new((x0, y, z)) for y, z in profile]
        right = [self.bm.verts.new((x1, y, z)) for y, z in profile]
        faces = [self.bm.faces.new(list(reversed(left))), self.bm.faces.new(right)]
        n = len(profile)
        for i in range(n):
            j = (i + 1) % n
            faces.append(self.bm.faces.new([left[i], left[j], right[j], right[i]]))
        for f in faces:
            f.material_index = MATERIALS.index(mat)

    def to_object(self, name, coll):
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        mesh = bpy.data.meshes.new(name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        for m in MATERIALS:
            mesh.materials.append(bpy.data.materials[m])
        obj = bpy.data.objects.new(name, mesh)
        coll.objects.link(obj)
        return obj


def clear_file():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    for coll in list(bpy.data.collections):
        bpy.data.collections.remove(coll)
    for block in (bpy.data.meshes, bpy.data.armatures, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for item in list(block):
            block.remove(item)
    for name, color in COLORS.items():
        mat = bpy.data.materials.new(name)
        mat.diffuse_color = (*color, 1)
        bsdf = mat.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = (*color, 1)
        bsdf.inputs["Roughness"].default_value = 0.7
        bsdf.inputs["Metallic"].default_value = 0.3


def save_and_export(name, coll):
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, name + ".blend"))
    with bpy.context.temp_override(collection=coll):
        bpy.ops.collection.exporter_add(name="IO_FH_gltf2")
    props = coll.exporters[0].export_properties
    props.filepath = "//../" + name + ".glb"
    props.export_format = 'GLB'
    bpy.ops.wm.save_mainfile()
    bpy.ops.wm.collection_export_all()


def side_wall(frame, x0, x1, bottom_back, bottom_front):
    """
    A side wall plate reaching OVER_GRATE above the grate and the chute, with a trim strip on top. Along the grate
    its bottom edge runs from bottom_back to bottom_front; along the chute it goes down to the floor.
    """
    top_back = grate_top(0) + OVER_GRATE
    top_mid = grate_top(GRATE) + OVER_GRATE
    top_front = CHUTE_BOTTOM + OVER_GRATE
    frame.prism([(0, bottom_back), (GRATE, bottom_front), (GRATE, top_mid), (0, top_back)], x0, x1, "M_Panel")
    frame.prism([(GRATE, 0), (LENGTH, 0), (LENGTH, top_front), (GRATE, top_mid)], x0, x1, "M_Panel")
    frame.prism([(0, top_back), (GRATE, top_mid), (GRATE, top_mid + TRIM), (0, top_back + TRIM)],
                x0 - 0.01, x1 + 0.01, "M_Trim")
    frame.prism([(GRATE, top_mid), (LENGTH, top_front), (LENGTH, top_front + TRIM), (GRATE, top_mid + TRIM)],
                x0 - 0.01, x1 + 0.01, "M_Trim")


def build():
    clear_file()
    coll = bpy.data.collections.new("grizzly_grate")
    bpy.context.scene.collection.children.link(coll)

    frame = Mesh()
    # Plinth under the tray, with a trim band round its top.
    frame.box((0, 0, 0), (2, GRATE, TRAY_TOP - 0.03), "M_Frame")
    frame.box((-0.005, -0.005, TRAY_TOP - 0.11), (2.005, GRATE, TRAY_TOP - 0.07), "M_Trim")
    # The tray's sides, below its belt surface.
    frame.box((WALL, WALL, TRAY_TOP - 0.03), (2, GRATE - WALL, TRAY_TOP - 0.005), "M_Frame")

    # Left wall: the whole height, from the tray to above the grate.
    side_wall(frame, 0, WALL, TRAY_TOP, TRAY_TOP)
    # Right wall: only above the grate; under it the side is open for the fines to leave.
    side_wall(frame, 2 - WALL, 2, grate_top(0) - UNDER_GRATE, grate_top(GRATE) - UNDER_GRATE)

    # Back wall between the tray and the grate, keeping the fines on the tray.
    frame.box((0, 0, TRAY_TOP), (2, WALL, grate_top(0) - UNDER_GRATE), "M_Panel")
    # The grate's cross member at the back, under the bars.
    frame.box((WALL, 0, grate_top(0) - UNDER_GRATE), (2 - WALL, 0.1, grate_top(0) - 0.09), "M_Frame")
    # The chute: a solid ramp whose back face is the tray's front wall, sloping from under the bars' ends down to
    # the front face, with a trim band across its back.
    frame.prism([(GRATE - WALL, 0), (LENGTH, 0), (LENGTH, CHUTE_BOTTOM), (GRATE, CHUTE_TOP), (GRATE - WALL, CHUTE_TOP)],
                WALL, 2 - WALL, "M_Frame")
    frame.box((WALL, GRATE - WALL - 0.005, CHUTE_TOP - 0.12), (2 - WALL, GRATE - WALL, CHUTE_TOP - 0.06), "M_Trim")
    frame.to_object("Frame", coll)

    # The tray's belt surface: one quad, u across (Y), v in metres along the flow (+X).
    bm = bmesh.new()
    corners = [(WALL, WALL), (2, WALL), (2, GRATE - WALL), (WALL, GRATE - WALL)]
    face = bm.faces.new([bm.verts.new((x, y, TRAY_TOP)) for x, y in corners])
    uv = bm.loops.layers.uv.new("UVMap")
    for loop in face.loops:
        loop[uv].uv = (loop.vert.co.y, loop.vert.co.x)
    belt_mesh = bpy.data.meshes.new("Belt")
    bm.to_mesh(belt_mesh)
    bm.free()
    belt_mesh.materials.append(bpy.data.materials["M_ConvBelt"])
    if belt_mesh.polygons[0].normal.z < 0:
        belt_mesh.flip_normals()
    coll.objects.link(bpy.data.objects.new("Belt", belt_mesh))

    save_and_export("grizzly_grate", coll)


build()
