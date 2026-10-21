"""
Scooper arm blockouts. Blender frame: front = +Y, z up, origin at the anchor cell's left-bottom-back corner.
Run in Blender (MCP or the Text Editor): it empties the open file, then builds, saves and exports each form.

  scooper_arm.glb       2x2x4 pedestal (x 0..2, y 0..2, z 0..4). The Hub turret yaws about Z: it scoops from the
                        cell on its left (-X) and dumps on its right (+X).
  scooper_arm_long.glb  2x4x3 frame (x 0..2, y 0..4, z 0..3). It scoops in the front pit (+Y, walled, filled from
                        above), lifts over the top and dumps down the back chute (-Y). The Hub drum pitches about X.

Each has a static "Frame" mesh and a skinned "Arm" mesh on the "Rig" armature, bones Hub > Boom > Stick > Bucket,
every vertex weighted fully to one bone. The bucket is a dustpan: its back wall runs down the Bucket bone, and its
floor (at the bone's tail) reaches toward the machine (-Y in the rest pose), open on top and at the front.
"""
import bpy, bmesh, math, os
from mathutils import Vector, Matrix

HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
        else r"C:\Users\steph\OneDrive\Documents\godot\projects\gmp-framework\game\assets\models\machines\logistics\source")
OUT_DIR = os.path.dirname(HERE)

COLORS = {
    "M_Frame": (0.30, 0.31, 0.33),
    "M_Hub": (0.45, 0.16, 0.08),
    "M_Arm": (0.85, 0.58, 0.08),
    "M_Bucket": (0.13, 0.13, 0.14),
}
MATERIALS = list(COLORS)

T = 0.05  # bucket plate thickness


class Mesh:
    """A bmesh being built from boxes, each box on one material and (for skinned meshes) one bone."""

    def __init__(self):
        self.bm = bmesh.new()
        self.deform = self.bm.verts.layers.deform.verify()
        self.groups = []

    def box(self, center, size, mat, bone=None, rotation=Matrix.Identity(3)):
        verts = []
        for corner in [(-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1), (-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)]:
            local = Vector((corner[0] * size[0], corner[1] * size[1], corner[2] * size[2])) / 2
            verts.append(self.bm.verts.new(Vector(center) + rotation @ local))
        for face in [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]:
            f = self.bm.faces.new([verts[i] for i in face])
            f.material_index = MATERIALS.index(mat)
        if bone is not None:
            if bone not in self.groups:
                self.groups.append(bone)
            for v in verts:
                v[self.deform][self.groups.index(bone)] = 1.0

    def beam(self, p0, p1, width, mat, bone):
        """A square box from p0 to p1."""
        p0, p1 = Vector(p0), Vector(p1)
        rotation = (p1 - p0).to_track_quat('Y', 'Z').to_matrix()
        self.box((p0 + p1) / 2, (width, (p1 - p0).length, width), mat, bone, rotation)

    def cylinder_x(self, center, radius, length, mat, bone=None, sides=12):
        before = set(self.bm.verts)
        bmesh.ops.create_cone(self.bm, cap_ends=True, segments=sides, radius1=radius, radius2=radius, depth=length,
                              matrix=Matrix.Translation(center) @ Matrix.Rotation(math.pi / 2, 4, 'Y'))
        new = [v for v in self.bm.verts if v not in before]
        for f in {f for v in new for f in v.link_faces}:
            f.material_index = MATERIALS.index(mat)
        if bone is not None:
            if bone not in self.groups:
                self.groups.append(bone)
            for v in new:
                v[self.deform][self.groups.index(bone)] = 1.0

    def bucket(self, pivot, length, floor, width, mat, bone):
        """The dustpan hanging straight down from pivot, its floor reaching toward -Y."""
        p = Vector(pivot)
        side = length * 0.7
        self.box(p + Vector((0, T / 2, -length / 2)), (width, T, length), mat, bone)
        self.box(p + Vector((0, (T - floor) / 2, -length - T / 2)), (width, floor + T, T), mat, bone)
        for x in (-1, 1):
            self.box(p + Vector((x * (width - T) / 2, (T - floor) / 2, -length + side / 2)), (T, floor + T, side), mat, bone)

    def to_object(self, name, coll):
        mesh = bpy.data.meshes.new(name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        for m in MATERIALS:
            mesh.materials.append(bpy.data.materials[m])
        obj = bpy.data.objects.new(name, mesh)
        coll.objects.link(obj)
        for bone in self.groups:
            obj.vertex_groups.new(name=bone)
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


def make_rig(coll, bones):
    """bones: (name, parent, head, tail) in order. Roll 0 everywhere."""
    arm = bpy.data.armatures.new("Rig")
    rig = bpy.data.objects.new("Rig", arm)
    coll.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    for name, parent, head, tail in bones:
        b = arm.edit_bones.new(name)
        b.head = head
        b.tail = tail
        b.roll = 0
        if parent:
            b.parent = arm.edit_bones[parent]
    bpy.ops.object.mode_set(mode='OBJECT')
    return rig


def skin(obj, rig):
    obj.parent = rig
    mod = obj.modifiers.new("Armature", 'ARMATURE')
    mod.object = rig


def arm_chain(hub_head, boom_head, boom_len, stick_len, bucket_len):
    """Rest pose bones, the arm reaching forward (+Y): boom 35 degrees up, stick 60 degrees down, bucket straight down."""
    up = math.radians(35)
    down = math.radians(60)
    elbow = Vector(boom_head) + Vector((0, math.cos(up), math.sin(up))) * boom_len
    tip = elbow + Vector((0, math.cos(down), -math.sin(down))) * stick_len
    return [
        ("Hub", None, Vector(hub_head), Vector(hub_head) + Vector((0, 0.5, 0))),
        ("Boom", "Hub", Vector(boom_head), elbow),
        ("Stick", "Boom", elbow, tip),
        ("Bucket", "Stick", tip, tip + Vector((0, 0, -bucket_len))),
    ]


def save_and_export(name, coll):
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, name + ".blend"))
    with bpy.context.temp_override(collection=coll):
        bpy.ops.collection.exporter_add(name="IO_FH_gltf2")
    props = coll.exporters[0].export_properties
    props.filepath = "//../" + name + ".glb"
    props.export_format = 'GLB'
    bpy.ops.wm.save_mainfile()
    bpy.ops.wm.collection_export_all()


def build_pedestal():
    clear_file()
    coll = bpy.data.collections.new("scooper_arm")
    bpy.context.scene.collection.children.link(coll)

    frame = Mesh()
    frame.box((1, 1, 0.15), (1.8, 1.8, 0.3), "M_Frame")
    frame.box((1, 1, 1.65), (0.8, 0.8, 2.7), "M_Frame")
    frame.box((1, 1, 2.95), (1.0, 1.0, 0.1), "M_Frame")

    bones = arm_chain(hub_head=(1, 1, 3.3), boom_head=(1, 1, 3.75), boom_len=2.0, stick_len=1.8, bucket_len=0.8)
    arm = Mesh()
    arm.box((1, 1, 3.3), (1.2, 1.2, 0.6), "M_Hub", "Hub")
    arm.box((1, 0.55, 3.35), (0.9, 0.5, 0.5), "M_Frame", "Hub")
    arm.box((1, 1, 3.7), (0.35, 0.35, 0.2), "M_Hub", "Hub")
    arm.beam(bones[1][2], bones[1][3], 0.24, "M_Arm", "Boom")
    arm.beam(bones[2][2], bones[2][3], 0.18, "M_Arm", "Stick")
    arm.bucket(bones[3][2], 0.8, 0.9, 1.6, "M_Bucket", "Bucket")

    rig = make_rig(coll, bones)
    frame.to_object("Frame", coll)
    skin(arm.to_object("Arm", coll), rig)
    save_and_export("scooper_arm", coll)


def build_long():
    clear_file()
    coll = bpy.data.collections.new("scooper_arm_long")
    bpy.context.scene.collection.children.link(coll)

    frame = Mesh()
    # Side walls the whole length, keeping items in the pit and on the chute.
    for x in (0.06, 1.94):
        frame.box((x, 2, 0.2), (0.12, 4, 0.4), "M_Frame")
    # Pit (front cell): a floor plate, a front wall and a stop wall toward the machine. It's filled from above.
    frame.box((1, 3.2, 0.025), (1.76, 1.6, 0.05), "M_Frame")
    frame.box((1, 3.95, 0.2), (1.76, 0.1, 0.4), "M_Frame")
    frame.box((1, 2.35, 0.25), (1.76, 0.1, 0.5), "M_Frame")
    # Posts and axle carrying the hub.
    for x in (0.2, 1.8):
        frame.box((x, 2, 0.85), (0.16, 0.3, 1.7), "M_Frame")
    frame.cylinder_x(Vector((1, 2, 1.7)), 0.08, 1.6, "M_Frame")
    # Chute (back cell): slopes down toward -Y and out the back, steep enough (40 degrees) for ingots to slide.
    slope = math.atan2(1.1, 1.3)
    frame.box((1, 0.65, 0.65), (1.76, math.hypot(1.3, 1.1), 0.05), "M_Frame", rotation=Matrix.Rotation(slope, 3, 'X'))

    bones = arm_chain(hub_head=(1, 2, 1.7), boom_head=(1, 2, 1.7), boom_len=1.25, stick_len=1.15, bucket_len=0.65)
    arm = Mesh()
    arm.cylinder_x(Vector((1, 2, 1.7)), 0.3, 0.5, "M_Hub", "Hub")
    arm.box((1, 2.3, 1.7), (0.3, 0.3, 0.3), "M_Hub", "Hub")
    arm.beam(bones[1][2], bones[1][3], 0.2, "M_Arm", "Boom")
    arm.beam(bones[2][2], bones[2][3], 0.16, "M_Arm", "Stick")
    arm.bucket(bones[3][2], 0.65, 0.75, 1.4, "M_Bucket", "Bucket")

    rig = make_rig(coll, bones)
    frame.to_object("Frame", coll)
    skin(arm.to_object("Arm", coll), rig)
    save_and_export("scooper_arm_long", coll)


build_pedestal()
build_long()
