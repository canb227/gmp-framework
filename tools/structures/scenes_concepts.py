"""
Concept structure scenes (proof of concept / visual reference): the models from build_concepts.py with rough
colliders, their baked "idle-loop" animation autoplaying, and a few cheap approximations of their behaviour:

  bounce pad        springy pad surface (restitution)
  vortex funnel     bowl segments with a swirling surface velocity, so items orbit down to the hole
  pneumatic tubes   walls that push items along the tube at TUBE_SPEED
  screw elevator    walls that carry items up the tube
  heat lamp / cryo  working conveyor belts (tagging items HOT / COLD needs a script)

They have no blueprints (blueprintItemID is empty): they're placed in the museum's Concept Lab.
"""
import math
from scenegen import *
from scenes_conveyors import straight_cols, BASIC_SPEED, BASIC_WALL
from scenes_chutes import funnel_walls

IMPORT = "res://game/assets/models/shared/salvage_import.gd"
M = "res://game/assets/models/machines/concepts/"
D = "game/scenes/structures/concepts/"
TUBE_R, TUBE_SPEED = 0.72, 6.0

def scene(name, glb, cells=((0, 0, 0),), arrow=0):
    sc = Scene(name, M + glb, IMPORT)
    sc.root("structure", "", cells=cells, arrow=arrow)
    sc.model()
    sc.autoplay()
    return sc

def cells(w, l, h, centred=False):
    xs = range(-(w // 2), w // 2 + 1) if centred else range(w)
    zs = range(-(l // 2), l // 2 + 1) if centred else range(0, -l, -1)
    return [(x, y, z) for y in range(h) for z in zs for x in xs]

def tube_ring(sc, prefix, c, t, s, length, speed=TUBE_SPEED, skip=(), curve=0.0):
    """Eight wall panels round a tube segment centred on c (Blender), running along t with side vector s;
    each pushes along the tube. curve (1/radius, bending towards +s) shortens the inner panels of a bend."""
    up = cross(s, t)
    for j in range(8):
        if j in skip:
            continue
        a = j / 8 * math.tau
        d = add(mul(s, math.cos(a)), mul(up, math.sin(a)))
        tv = add(mul(s, -math.sin(a)), mul(up, math.cos(a)))
        sc.obox_b(f"{prefix}{j}", add(c, mul(d, TUBE_R + 0.03)), mul(t, length * (1 - curve * (TUBE_R + 0.05) * math.cos(a))), mul(tv, 2 * TUBE_R * math.tan(math.pi / 8) + 0.02),
                  mul(d, 0.05), friction=0.05, tangent=b2g(mul(t, speed)))

# ---------------------------------------------------------------------------- pieces
def gravity_inverter():
    sc = scene("ConceptGravityInverter", "concept_gravity_inverter.glb")
    sc.box_b("Base", (0, 0, -0.95), (1.94, 1.94, 0.1))
    for i, (x, y) in enumerate(((-0.82, -0.82), (0.82, -0.82), (0.82, 0.82), (-0.82, 0.82))):
        sc.box_b(f"Pylon{i}", (x, y, -0.5), (0.16, 0.16, 0.8))
    sc.sensor_b("FieldTrigger", (0, 0, 0.05), (1.8, 1.8, 1.8))
    return sc

def tag_gate():
    sc = scene("ConceptTagGate", "concept_tag_gate.glb", arrow=1)
    straight_cols(sc, BASIC_SPEED, BASIC_WALL)
    for sx in (-1, 1):
        sc.box_b(f"Post{'LR'[sx > 0]}", (sx * 0.93, 0, (BELT_TOP + 0.45) / 2 + 0.07), (0.12, 0.22, 0.45 - BELT_TOP - 0.14))
    sc.box_b("Lintel", (0, 0, 0.53), (1.98, 0.24, 0.16))
    sc.sensor_b("Scan", (0, -0.4, BELT_TOP + 0.45), (1.6, 0.6, 0.8))
    return sc

def bounce_pad():
    sc = scene("ConceptBouncePad", "concept_bounce_pad.glb")
    sc.box_b("Base", (0, 0, -0.8), (1.9, 1.9, 0.4))
    sc.box_b("Pad", (0, 0, -0.24), (1.7, 1.7, 0.12), friction=0.9, material=TAG_RUBBER, extra=("restitution = 1.25",))
    return sc

def vortex_funnel():
    sc = scene("ConceptVortexFunnel", "concept_vortex_funnel.glb", cells=cells(2, 2, 1))
    cx, cy = 1.0, 1.0
    r0, z0, r1, z1 = 0.45, -0.4, 1.76, 0.85
    for k in range(8):
        a = (k + 0.5) / 8 * math.tau
        d = (math.cos(a), math.sin(a), 0); t = (-math.sin(a), math.cos(a), 0)
        inner = (cx + d[0] * r0, cy + d[1] * r0, z0); outer = (cx + d[0] * r1, cy + d[1] * r1, z1)
        v = sub(outer, inner)
        n = norm(cross(v, t))
        if n[2] > 0: n = mul(n, -1)                           # grow down / outward, under the bowl surface
        width = 2 * r1 * math.tan(math.pi / 8)
        sc.obox_b(f"Bowl{k}", add(mul(add(inner, outer), 0.5), mul(n, 0.03)), mul(t, width), v, mul(n, 0.06),
                  friction=0.3, tangent=b2g(mul(t, 2.5)))
    for i, (x, y) in enumerate(((-0.9, -0.9), (2.9, -0.9), (2.9, 2.9), (-0.9, 2.9))):
        sc.box_b(f"Leg{i}", (x, y, -0.1), (0.12, 0.12, 1.8))
    return sc

def tube_straight():
    sc = scene("ConceptTubeStraight", "concept_tube_straight.glb", arrow=1)
    tube_ring(sc, "Wall", (0, 0, 0), (0, 1, 0), (1, 0, 0), 1.96)
    sc.box_b("Leg", (0, 0, -0.86), (0.12, 0.12, 0.28))
    return sc

def tube_bend():
    sc = scene("ConceptTubeBend", "concept_tube_bend.glb", arrow=3)
    piv = (1, -1, 0)
    n = 5
    for i in range(n):
        th = math.pi / 2 * (i + 0.5) / n
        c = (piv[0] - math.cos(th), piv[1] + math.sin(th), 0)
        t = (math.sin(th), math.cos(th), 0); s = (math.cos(th), -math.sin(th), 0)
        seg, k = 2 * math.sin(math.pi / 4 / n) * 1.02, 1.0
        if i == 0:                                            # straight stubs at the faces keep the walls in the cell
            c, t, s, seg, k = (0, -0.85, 0), (0, 1, 0), (1, 0, 0), 0.28, 0.0
        elif i == n - 1:
            c, t, s, seg, k = (0.85, 0, 0), (1, 0, 0), (0, -1, 0), 0.28, 0.0
        tube_ring(sc, f"Seg{i}Wall", c, t, s, seg, curve=k)
    return sc

def tube_junction():
    sc = scene("ConceptTubeJunction", "concept_tube_junction.glb", arrow=1)
    tube_ring(sc, "Main", (0, 0, 0), (0, 1, 0), (1, 0, 0), 1.96, skip=(0,))
    tube_ring(sc, "Branch", (0.87, 0, 0), (1, 0, 0), (0, -1, 0), 0.2)
    sc.body("FlapBody")
    sc.box("Flap", (0.04, 1.2, 0.7), (0, 0, -0.35), parent="FlapBody", friction=0.05)
    return sc

def tube_receiver():
    sc = scene("ConceptTubeReceiver", "concept_tube_receiver.glb", arrow=1)
    tube_ring(sc, "Wall", (0, -0.6, 0), (0, 1, 0), (1, 0, 0), 0.7)
    for sx in (-1, 1):
        sc.box_b(f"Side{'LR'[sx > 0]}", (sx * 0.88, 0.1, -0.025), (0.05, 0.7, 1.9))
    sc.box_b("Roof", (0, 0.1, 0.9), (1.8, 0.7, 0.05))
    sc.box_b("Back", (0, -0.23, 0.55), (1.8, 0.05, 0.7))
    a = math.radians(-12)
    sc.obox_b("Slide", (0, 0.32, -0.74), (1.4, 0, 0), (0, 1.3 * math.cos(a), 1.3 * math.sin(a)), (0, -0.04 * math.sin(a), 0.04 * math.cos(a)), friction=0.1)
    return sc

def emitter(name, glb):
    sc = scene(name, glb, arrow=1)
    straight_cols(sc, BASIC_SPEED, BASIC_WALL)
    for sx in (-1, 1):
        sc.box_b(f"Post{'LR'[sx > 0]}", (sx * 0.96, 0, (BELT_TOP + 0.62) / 2 + 0.08), (0.07, 0.14, 0.62 - BELT_TOP - 0.16))
    sc.box_b("Beam", (0, 0, 0.62), (1.98, 0.16, 0.1))
    sc.sensor_b("Zone", (0, 0, BELT_TOP + 0.5), (1.6, 1.6, 0.9))
    return sc

def counterweight_elevator():
    sc = scene("ConceptCounterweightElevator", "concept_counterweight_elevator.glb", cells=cells(1, 1, 3))
    for i, (x, y) in enumerate(((-0.92, -0.92), (0.92, -0.92), (0.92, 0.92), (-0.92, 0.92))):
        sc.box_b(f"Post{i}", (x, y, 2.0), (0.1, 0.1, 5.96))
    sc.box_b("Head", (0, 0, 4.9), (1.98, 1.98, 0.14))
    sc.box_b("Base", (0, 0, -0.97), (1.98, 1.98, 0.06))
    for name, x, z in (("CageABody", -0.45, 3.1), ("CageBBody", 0.45, -0.9)):
        sc.body(name, b2g((x, 0, z)))
        sc.box("Floor", (0.84, 0.06, 1.7), (0, 0, 0), parent=name)
    return sc

def rail_gun():
    sc = scene("ConceptRailGun", "concept_rail_gun.glb", cells=cells(1, 4, 1), arrow=1)
    sc.box_b("Base", (0, 3.0, -0.95), (1.9, 7.9, 0.1))
    for sx in (-1, 1):
        sc.box_b(f"Rail{'LR'[sx > 0]}", (sx * 0.38, 3.0, -0.3), (0.14, 7.7, 0.2))
        sc.box_b(f"Capacitors{'LR'[sx > 0]}", (sx * 0.8, 2.95, -0.6), (0.26, 5.8, 0.6))
    sc.box_b("Breech", (0, -0.6, -0.4), (1.2, 0.7, 0.5))
    sc.sensor_b("Loader", (0, -0.6, -0.05), (0.9, 0.6, 0.3))
    return sc

def tipping_bucket():
    sc = scene("ConceptTippingBucket", "concept_tipping_bucket.glb", cells=cells(1, 1, 2))
    sc.box_b("Base", (0, 0, -0.97), (1.98, 1.98, 0.06))
    funnel_walls(sc, (-0.9, 0.9, -0.9, 0.9), (-0.3, 0.3, -0.3, 0.3), 2.95, 2.3)
    for sx in (-1, 1):
        t = math.radians(18) * sx
        sc.obox_b(f"Slide{'LR'[sx > 0]}", (sx * 0.6, 0, -0.55), (0.8 * math.cos(t), 0, -0.8 * math.sin(t)), (0, 1.5, 0),
                  (0.04 * math.sin(t), 0, 0.04 * math.cos(t)), friction=0.1)
    # the bucket, posed at rest (tipped +28 deg about Blender Y = Godot -Z)
    sc.body("BucketBody", b2g((0, 0, 0.9)), rot_z(-math.radians(28)))
    for sx in (-1, 1):
        sc.box(f"Scoop{'LR'[sx > 0]}", (0.8, 0.04, 1.4), (sx * 0.4, -0.18, 0), parent="BucketBody")
        sc.box(f"Lip{'LR'[sx > 0]}", (0.04, 0.36, 1.4), (sx * 0.79, -0.02, 0), parent="BucketBody")
    sc.box("Divider", (0.06, 0.5, 1.4), (0, 0.05, 0), parent="BucketBody")
    return sc

def assembly_chamber():
    sc = scene("ConceptAssemblyChamber", "concept_assembly_chamber.glb", cells=cells(3, 3, 3, centred=True))
    sc.box_b("Floor", (0, 0, -0.97), (5.9, 5.9, 0.06))
    sc.box_b("Roof", (0, 0, 4.9), (5.9, 5.9, 0.1))
    for sx in (-1, 1):
        sc.box_b(f"WallX{'LR'[sx > 0]}", (sx * 2.9, 0, 1.95), (0.14, 5.9, 5.8))
        sc.box_b(f"WallY{'BF'[sx > 0]}", (0, sx * 2.9, 1.95), (5.62, 0.14, 5.8))
    sc.sensor_b("Chamber", (0, 0, 1.95), (5.4, 5.4, 5.6))
    return sc

def screw_elevator():
    sc = scene("ConceptScrewElevator", "concept_screw_elevator.glb", cells=cells(1, 1, 3))
    sc.box_b("Base", (0, 0, -0.78), (1.9, 1.9, 0.44))
    tube_ring(sc, "Wall", (0, 0, 2.08), (0, 0, 1), (1, 0, 0), 5.2, speed=1.5)
    sc.box_b("Cap", (0, 0, 4.85), (1.0, 1.0, 0.25))
    return sc

def platform_elevator():
    sc = scene("ConceptPlatformElevator", "concept_platform_elevator.glb", cells=cells(1, 2, 3))
    for sx in (-1, 1):
        sc.box_b(f"Side{'LR'[sx > 0]}", (sx * 0.95, 1.0, 2.0), (0.06, 3.96, 5.96))
    sc.box_b("Base", (0, 1.0, -0.97), (1.98, 3.96, 0.06))
    return sc

def concepts():
    out = []
    for fn, glb in ((gravity_inverter, None), (tag_gate, None), (bounce_pad, None), (vortex_funnel, None),
                    (tube_straight, None), (tube_bend, None), (tube_junction, None), (tube_receiver, None),
                    (lambda: emitter("ConceptHeatLamp", "concept_heat_lamp.glb"), None),
                    (lambda: emitter("ConceptCryoVent", "concept_cryo_vent.glb"), None),
                    (counterweight_elevator, None), (rail_gun, None), (tipping_bucket, None), (assembly_chamber, None),
                    (screw_elevator, None), (platform_elevator, None)):
        sc = fn()
        out.append(sc.write(D + sc.name + ".tscn"))
    return out
