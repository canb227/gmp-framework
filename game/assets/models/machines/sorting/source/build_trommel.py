"""
Trommel frame blockout. Blender frame: front = +Y, z up, origin at the anchor cell's left-bottom-back corner.
Run in Blender (MCP or the Text Editor) or from the command line with --background --python: it empties the open
file, then builds, saves and exports.

  trommel.glb  4x8x4 frame (x 0..4, y 0..8, z 0..4). The drum isn't modelled: TrommelDrum makes it in Godot from its
               settings. It turns about an axis at x 1.25, from (y 0.9, z 2.65) tilted 4 degrees down toward the front,
               6 m long, about 1.28 m to the outside of its hoops.
               - Feed comes in over the left half of the back face (x 0..2, from an up-slope conveyor whose belt top is
                 at z 2.15) down an inlet chute into the drum's open back end.
               - Fines fall through the drum onto a belt tray (top at z 0.5, running +X) that throws them out of the
                 open lower right side, over the wall of a belt laid alongside.
               - Oversize tumbles out of the drum's open front end onto a chute that brings it down to belt height at
                 the left half of the front face.

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

WIDTH = 4.0
LENGTH = 8.0
WALL = 0.06           # wall plate thickness
TRIM = 0.04           # the yellow strip along wall tops
TRAY_TOP = 0.5
TRAY_END = 6.5        # the tray runs from the back to here; the oversize chute takes the rest, starting under
                      # the last half metre of the drum so nothing leaving its front end can drop back onto the tray
WALL_TOP = 2.8        # side and back walls
RIGHT_BOTTOM = 1.15   # the right wall stops here, leaving the side open above the tray for the fines
LANE = 2.0            # feed and oversize use the left half (x 0..2)

INLET_BACK = 2.08     # inlet chute surface at the back face, just under the feed belt's top (2.15)
INLET_FRONT = 1.95    # and where it ends, inside the drum's back end
INLET_END = 0.88     # just short of the drum's back end (0.9), clear of its lifters
INLET_RIGHT = 2.5    # the chute covers the drum's whole open back end, so nothing falls out of it onto the tray

CHUTE_TOP = 0.9       # oversize chute: under the drum's front hoop, down to a little above a belt's top (0.15)
CHUTE_BOTTOM = 0.22
CHUTE_RIGHT = 2.5     # the chute is wider than the lane at its top; a guide wall funnels it into x 0..2

BEAM_Z = 3.88         # overhead beams the drum hangs from
BEAMS_Y = (1.0, 6.6)


class Mesh:
    """A bmesh being built from boxes, prisms and plates, each on one material."""

    def __init__(self):
        self.bm = bmesh.new()

    def box(self, lo, hi, mat):
        """An axis-aligned box from corner lo to corner hi."""
        self.prism([(lo[1], lo[2]), (hi[1], lo[2]), (hi[1], hi[2]), (lo[1], hi[2])], lo[0], hi[0], mat)

    def prism(self, profile, x0, x1, mat):
        """A convex (y, z) profile pushed out from x0 to x1."""
        left = [self.bm.verts.new((x0, y, z)) for y, z in profile]
        right = [self.bm.verts.new((x1, y, z)) for y, z in profile]
        self.faces(left, right, mat)

    def plate(self, start, end, bottoms, tops, thickness, mat):
        """
        An upright plate along the floor line from start (x, y) to end, its bottom and top edges at bottoms[0]/tops[0]
        over start and bottoms[1]/tops[1] over end, thickness to its left (seen from start looking at end).
        """
        a, b = Vector(start), Vector(end)
        side = Vector((-(b - a).y, (b - a).x)).normalized() * thickness
        outline = [(a, bottoms[0]), (b, bottoms[1]), (b, tops[1]), (a, tops[0])]
        near = [self.bm.verts.new((p.x, p.y, z)) for p, z in outline]
        far = [self.bm.verts.new((p.x + side.x, p.y + side.y, z)) for p, z in outline]
        self.faces(near, far, mat)

    def faces(self, near, far, mat):
        made = [self.bm.faces.new(list(reversed(near))), self.bm.faces.new(far)]
        n = len(near)
        for i in range(n):
            j = (i + 1) % n
            made.append(self.bm.faces.new([near[i], near[j], far[j], far[i]]))
        for f in made:
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


def build():
    clear_file()
    coll = bpy.data.collections.new("trommel")
    bpy.context.scene.collection.children.link(coll)

    frame = Mesh()
    # Plinth under the tray, with a trim band round its top, and the tray's sides below its belt surface.
    frame.box((0, 0, 0), (WIDTH, TRAY_END, TRAY_TOP - 0.03), "M_Frame")
    frame.box((-0.005, -0.005, TRAY_TOP - 0.11), (WIDTH + 0.005, TRAY_END, TRAY_TOP - 0.07), "M_Trim")
    frame.box((WALL, WALL, TRAY_TOP - 0.03), (WIDTH, TRAY_END, TRAY_TOP - 0.005), "M_Frame")

    # Side walls, the right one only above the tray's open side, with trim along their tops.
    frame.box((0, 0, TRAY_TOP - 0.03), (WALL, LENGTH, WALL_TOP), "M_Panel")
    frame.box((-0.01, 0, WALL_TOP), (WALL + 0.01, LENGTH, WALL_TOP + TRIM), "M_Trim")
    frame.box((WIDTH - WALL, 0, RIGHT_BOTTOM), (WIDTH, TRAY_END, WALL_TOP), "M_Panel")
    frame.box((WIDTH - WALL - 0.01, 0, WALL_TOP), (WIDTH + 0.01, TRAY_END, WALL_TOP + TRIM), "M_Trim")

    # Back wall: low across the whole width, and up to the wall tops beside the feed lane.
    frame.box((0, 0, TRAY_TOP), (WIDTH, WALL, INLET_BACK - 0.06), "M_Panel")
    frame.box((LANE, 0, INLET_BACK - 0.06), (WIDTH, WALL, WALL_TOP), "M_Panel")

    # Inlet chute from the feed belt into the drum's back end, with a guide wall angling the feed in from the left
    # (the drum is narrower than the lane at this height) and a rail along its right edge, which is past the lane's
    # so the chute covers the drum's whole open back end.
    frame.prism([(0, INLET_BACK - 0.06), (INLET_END, INLET_FRONT - 0.06), (INLET_END, INLET_FRONT), (0, INLET_BACK)],
                WALL, INLET_RIGHT, "M_Frame")
    frame.plate((WALL, 0.05), (0.68, INLET_END), (INLET_BACK, INLET_FRONT), (INLET_BACK + 0.5, INLET_FRONT + 0.5),
                -WALL, "M_Panel")
    frame.box((INLET_RIGHT - WALL, 0, INLET_FRONT), (INLET_RIGHT, INLET_END, WALL_TOP + 0.1), "M_Panel")
    # A skirt hanging from the chute's tip to just above the drum's bottom bars, so nothing rolls back out under it.
    frame.box((0.45, INLET_END - 0.05, 1.6), (INLET_RIGHT - 0.1, INLET_END, INLET_FRONT), "M_Frame")

    # Oversize chute: a solid ramp whose back face is the tray's front wall, from under the drum's front end down to
    # the front face, with a guide wall funnelling it into the lane. A block fills the corner beside it.
    frame.prism([(TRAY_END, 0), (LENGTH, 0), (LENGTH, CHUTE_BOTTOM), (TRAY_END + 0.05, CHUTE_TOP), (TRAY_END, CHUTE_TOP)],
                WALL, CHUTE_RIGHT, "M_Frame")
    frame.plate((CHUTE_RIGHT, TRAY_END + 0.05), (LANE, LENGTH), (CHUTE_TOP, CHUTE_BOTTOM), (CHUTE_TOP + 0.6, CHUTE_BOTTOM + 0.6),
                -WALL, "M_Panel")
    frame.box((CHUTE_RIGHT, TRAY_END, 0), (WIDTH, LENGTH, CHUTE_TOP), "M_Frame")
    frame.box((CHUTE_RIGHT, LENGTH - 0.06, CHUTE_TOP - 0.1), (WIDTH, LENGTH + 0.005, CHUTE_TOP - 0.05), "M_Trim")

    # Overhead beams the drum hangs from, on posts standing on the side walls.
    for y in BEAMS_Y:
        frame.box((0, y, BEAM_Z), (WIDTH, y + 0.15, 4.0), "M_Frame")
        frame.box((0, y, WALL_TOP + TRIM), (0.12, y + 0.15, BEAM_Z), "M_Frame")
        frame.box((WIDTH - 0.12, y, WALL_TOP + TRIM), (WIDTH, y + 0.15, BEAM_Z), "M_Frame")
        frame.box((1.2, y + 0.03, 3.6), (1.3, y + 0.12, BEAM_Z), "M_Trim")
    frame.to_object("Frame", coll)

    # The tray's belt surface: one quad, u across (Y), v in metres along the flow (+X).
    bm = bmesh.new()
    corners = [(WALL, WALL), (WIDTH, WALL), (WIDTH, TRAY_END), (WALL, TRAY_END)]
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

    save_and_export("trommel", coll)


build()
