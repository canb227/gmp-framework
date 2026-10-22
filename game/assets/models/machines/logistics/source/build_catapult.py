"""
Catapult blockout. Blender frame: front = +Y, z up, origin at the anchor cell's left-bottom-back corner.
Run in Blender (MCP or the Text Editor): it empties the open file, then builds, saves and exports catapult.glb.

  catapult.glb  2x4x4 (x 0..2, y 0..4, z 0..4). The bucket rests at the back on the floor, open on its left (-X)
                side (a lip at belt height) so a belt in the cell to the left feeds it. The Arm turns about X on the
                axle at AXLE: its throw is a negative rotation (the bucket goes up and over toward the front) until
                the beam hits the padded stop bar at STOP_ANGLE. A gauge on the right front shows the launch strength.

Nodes: Frame (static), Arm (origin on the axle), GaugeNeedle (origin on the dial's centre, turns about X: +100 degrees
is the weakest setting, -100 the strongest; it points up at 0).
"""
import bpy, bmesh, math, os
from mathutils import Vector, Matrix

HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
        else r"C:\Users\steph\OneDrive\Documents\godot\projects\gmp-framework\game\assets\models\machines\logistics\source")
OUT_DIR = os.path.dirname(HERE)

COLORS = {
    "M_Frame": (0.30, 0.31, 0.33),
    "M_Arm": (0.85, 0.58, 0.08),
    "M_Bucket": (0.13, 0.13, 0.14),
    "M_Weight": (0.35, 0.18, 0.10),
    "M_Pad": (0.04, 0.04, 0.04),
    "M_Dial": (0.85, 0.85, 0.80),
    "M_Low": (0.15, 0.60, 0.15),
    "M_High": (0.75, 0.10, 0.08),
}
MATERIALS = list(COLORS)

AXLE = Vector((1.0, 2.5, 1.0))
STOP_ANGLE = math.radians(-90)   # the Arm's X rotation when the beam rests on the stop bar
GAUGE = Vector((2.01, 3.7, 0.9))  # the dial's centre, on its face
GAUGE_SWEEP = math.radians(100)   # the needle turns from +this (weakest) to -this (strongest)
T = 0.05                          # bucket plate thickness


class Mesh:
    """A bmesh being built from boxes and cylinders, each on one material."""

    def __init__(self):
        self.bm = bmesh.new()

    def box(self, center, size, mat, rotation=Matrix.Identity(3)):
        verts = []
        for corner in [(-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1), (-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)]:
            local = Vector((corner[0] * size[0], corner[1] * size[1], corner[2] * size[2])) / 2
            verts.append(self.bm.verts.new(Vector(center) + rotation @ local))
        for face in [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]:
            f = self.bm.faces.new([verts[i] for i in face])
            f.material_index = MATERIALS.index(mat)

    def beam(self, p0, p1, width, mat):
        """A square box from p0 to p1."""
        p0, p1 = Vector(p0), Vector(p1)
        rotation = (p1 - p0).to_track_quat('Y', 'Z').to_matrix()
        self.box((p0 + p1) / 2, (width, (p1 - p0).length, width), mat, rotation)

    def cylinder_x(self, center, radius, length, mat, sides=12):
        before = set(self.bm.verts)
        bmesh.ops.create_cone(self.bm, cap_ends=True, segments=sides, radius1=radius, radius2=radius, depth=length,
                              matrix=Matrix.Translation(center) @ Matrix.Rotation(math.pi / 2, 4, 'Y'))
        new = [v for v in self.bm.verts if v not in before]
        for f in {f for v in new for f in v.link_faces}:
            f.material_index = MATERIALS.index(mat)

    def to_object(self, name, coll, origin=Vector((0, 0, 0))):
        """The mesh as an object whose origin is at origin (the geometry was built in the structure's frame)."""
        bmesh.ops.translate(self.bm, verts=self.bm.verts, vec=-origin)
        mesh = bpy.data.meshes.new(name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        for m in MATERIALS:
            mesh.materials.append(bpy.data.materials[m])
        obj = bpy.data.objects.new(name, mesh)
        obj.location = origin
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


def build_catapult():
    clear_file()
    coll = bpy.data.collections.new("catapult")
    bpy.context.scene.collection.children.link(coll)

    frame = Mesh()
    # An A-frame each side carrying the axle, on a skid.
    for x in (0.08, 1.92):
        frame.box((x, 2.5, 0.05), (0.16, 1.8, 0.1), "M_Frame")
        frame.beam((x, 1.7, 0.1), (x, AXLE.y, AXLE.z), 0.12, "M_Frame")
        frame.beam((x, 3.3, 0.1), (x, AXLE.y, AXLE.z), 0.12, "M_Frame")
        frame.box((x, AXLE.y, AXLE.z), (0.2, 0.26, 0.26), "M_Frame")
    frame.cylinder_x(AXLE, 0.05, 1.84, "M_Frame")
    # The stop bar the beam slams into, just ahead of where the beam lies at STOP_ANGLE, held up by a post each side.
    rest_dir = Vector((0, -0.894, -0.447))
    stop_dir = Matrix.Rotation(STOP_ANGLE, 3, 'X') @ rest_dir
    ahead = Vector((0, stop_dir.z, -stop_dir.y))  # the way the beam moves when it gets there
    bar = AXLE + stop_dir * 0.9 + ahead * 0.13
    bar.x = 1
    frame.box(bar, (1.7, 0.1, 0.1), "M_Frame")
    frame.box(bar - ahead * 0.06, (0.6, 0.04, 0.14), "M_Pad")
    for x in (0.1, 1.9):
        frame.beam((x, 3.3, 0.1), (x, bar.y, bar.z), 0.08, "M_Frame")
    # The strength gauge on the right front: a post, a dial facing +X, green to red marks round the needle's sweep.
    frame.box((1.96, GAUGE.y, 0.45), (0.08, 0.08, 0.9), "M_Frame")
    frame.box((GAUGE.x - 0.025, GAUGE.y, GAUGE.z), (0.03, 0.56, 0.5), "M_Dial")
    marks = 9
    for i in range(marks):
        t = i / (marks - 1)
        angle = GAUGE_SWEEP - t * 2 * GAUGE_SWEEP
        rotation = Matrix.Rotation(angle, 3, 'X')
        mat = "M_Low" if t < 0.5 else "M_High"
        frame.box(GAUGE + rotation @ Vector((0, 0, 0.2)), (0.01, 0.03, 0.06), mat, rotation)
    frame.to_object("Frame", coll)

    # The arm, built at rest in the structure's frame: the beam runs from the counterweight through the axle down
    # and back to the bucket's front wall. The bucket's floor lies flat on the ground at the back.
    arm = Mesh()
    arm.cylinder_x(AXLE, 0.12, 0.3, "M_Arm")
    arm.beam(AXLE - rest_dir * 0.7, (1, 1.4, 0.45), 0.16, "M_Arm")
    arm.box(AXLE - rest_dir * 0.7, (0.6, 0.5, 0.5), "M_Weight")
    arm.box((0.96, 0.8, 0.075), (1.88, 1.2, T), "M_Bucket")                    # floor
    arm.box((0.96, 0.225, 0.35), (1.88, T, 0.5), "M_Bucket")                   # back (outer) wall
    arm.box((0.96, 1.375, 0.35), (1.88, T, 0.5), "M_Bucket")                   # front (inner) wall
    arm.box((1.875, 0.8, 0.35), (T, 1.1, 0.5), "M_Bucket")                     # right wall
    arm.box((0.045, 0.8, 0.125), (T, 1.1, T), "M_Bucket")                      # left lip, at belt height
    arm.to_object("Arm", coll, AXLE)

    needle = Mesh()
    needle.box(GAUGE + Vector((0.01, 0, 0.09)), (0.02, 0.025, 0.22), "M_High")
    needle.cylinder_x(GAUGE + Vector((0.01, 0, 0)), 0.03, 0.03, "M_Pad")
    needle.to_object("GaugeNeedle", coll, GAUGE)

    save_and_export("catapult", coll)


build_catapult()
