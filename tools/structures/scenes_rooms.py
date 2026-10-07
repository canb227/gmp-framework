"""
Puzzle-room machines (models from game/assets/models/machines/rooms/source/build_rooms.py), with real colliders:

  SweepArm        a rubber blade hung 4 cm over the Carousel's turntable: riding items hit it and are scraped outward
  SlotSieve       a bar grate with 0.54 m slots over a hopper whose drag floor empties out of its open side
  TerraceCatcher  a padded catch trough whose drag floor empties out of its open end
  StormCollector  (electric chain) a strike pad that conveys items under a lightning mast; "Strike" sensor over the pad
  InsulatedBelt   (electric chain) a plain working belt on insulators
  CapacitorPress  (electric chain) a pass-through press; "Press" sensor under the ram
  BeltScraper     (sticky chain) a working belt with a blade at its head roller

They have no blueprints yet; place_museum.py places them in the Puzzle Rooms wing.
"""
import math
from scenegen import *
from scenes_concepts import scene, cells
from scenes_conveyors import straight_cols, BASIC_SPEED, BASIC_WALL

M = "res://game/assets/models/machines/rooms/"
D = "game/scenes/structures/rooms/"
ARM_A, ARM_B = (-1.5, 3.0), (-0.2, 11.0)          # as build_rooms.py
SIEVE_BARS = [-0.32 + 0.62 * k for k in range(9)]

def room_scene(name, glb, cell_list):
    sc = Scene(name, M + glb, "res://game/assets/models/shared/salvage_import.gd")
    sc.root("structure", "", cells=cell_list)
    sc.model()
    return sc

def cells_along(points):
    """Grid cells (Godot) touched by Blender-space points."""
    out = set()
    for p in points:
        g = b2g(p)
        out.add(tuple(int(math.floor((g[i] + 1) / 2)) for i in range(3)))
    return sorted(out)

def seg(sc, name, a, b, z0, z1, t, **kw):
    d = (b[0] - a[0], b[1] - a[1], 0)
    n = norm((-d[1], d[0], 0))
    sc.obox_b(name, ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2, (z0 + z1) / 2), d, mul(n, t), (0, 0, z1 - z0), **kw)

def sweep_arm():
    pts = [(0, 0, z) for z in (-0.9, 2.9)]
    for t in [i / 40 for i in range(41)]:
        for (a, b) in ((( 0, 0), ARM_A), (ARM_A, ARM_B)):
            x, y = a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t
            pts += [(x + dx, y + dy, z) for dx in (-0.2, 0.2) for dy in (-0.2, 0.2) for z in (0.04, 0.6, 2.92)]
    sc = room_scene("SweepArm", "sweep_arm.glb", cells_along(pts))
    sc.box_b("Pylon", (0, 0, 1.0), (0.8, 0.8, 4.0))
    seg(sc, "Blade", ARM_A, ARM_B, 0.04, 0.6, 0.12, friction=0.15)
    seg(sc, "BoomIn", (0, 0), ARM_A, 2.68, 2.92, 0.24)
    seg(sc, "BoomOut", ARM_A, ARM_B, 2.68, 2.92, 0.24)
    return sc

def slot_sieve():
    sc = room_scene("SlotSieve", "slot_sieve.glb", cells(3, 2, 1))
    sc.box_b("SideClosed", (4.95, 1.0, -0.6), (0.1, 4.0, 0.8))
    sc.box_b("SideRail", (-0.95, 1.0, -0.3), (0.1, 4.0, 0.2))
    for k, x in enumerate(SIEVE_BARS):
        sc.box_b(f"Bar{k}", (x, 1.0, -0.25), (0.08, 4.0, 0.1), friction=0.3)
    sc.box_b("HopperFloor", (2.0, 1.0, -0.975), (6.0, 4.0, 0.05), friction=0.6, tangent=b2g((-1.2, 0, 0)))
    for y in (-0.975, 2.975):
        sc.box_b(f"End{'BF'[y > 0]}", (2.0, y, -0.65), (6.0, 0.05, 0.7))
    sc.sensor_b("Undersize", (2.0, 1.0, -0.65), (5.6, 3.8, 0.5))
    return sc

def terrace_catcher():
    sc = room_scene("TerraceCatcher", "terrace_catcher.glb", cells(3, 1, 1))
    sc.box_b("Floor", (2.0, 0, -0.95), (6.0, 2.0, 0.1), friction=0.7, tangent=b2g((1.2, 0, 0)))
    sc.box_b("BackWall", (2.0, -0.95, -0.3), (6.0, 0.1, 1.2), extra=("restitution = 0.0",))
    sc.box_b("FrontLip", (2.0, 0.95, -0.75), (6.0, 0.1, 0.3))
    sc.box_b("EndClosed", (-0.95, 0, -0.55), (0.1, 2.0, 0.8))
    sc.sensor_b("Catch", (2.0, 0, -0.6), (5.8, 1.7, 0.6))
    return sc

def storm_collector():
    sc = room_scene("StormCollector", "storm_collector.glb", cells(1, 2, 3))
    sc.autoplay()
    sc.box_b("Pad", (0, 1.0, (BELT_TOP - 1) / 2), (1.9, 3.96, BELT_TOP + 1), friction=0.8, material=TAG_RUBBER,
             tangent=b2g((0, BASIC_SPEED, 0)))
    for sx in (-1, 1):
        sc.box_b(f"Cheek{'LR'[sx > 0]}", (sx * 0.94, 1.0, (BELT_TOP + 0.6) / 2), (0.04, 3.6, 0.6 - BELT_TOP), friction=0.1)
    for i, (x, y) in enumerate(((-0.92, -0.92), (0.92, -0.92), (0.92, 2.92), (-0.92, 2.92))):
        sc.box_b(f"Post{i}", (x, y, 0.45), (0.12, 0.12, 2.9))
    sc.box_b("Cage", (0, 1.0, 1.9), (1.96, 3.96, 0.12))
    sc.box_b("Mast", (0, 1.0, 3.3), (0.16, 0.16, 2.8))
    sc.sensor_b("Strike", (0, 1.0, BELT_TOP + 0.5), (1.7, 3.7, 0.9))
    return sc

def insulated_belt():
    sc = room_scene("InsulatedBelt", "insulated_belt.glb", [(0, 0, 0)])
    straight_cols(sc, BASIC_SPEED, BASIC_WALL)
    return sc

def capacitor_press():
    sc = room_scene("CapacitorPress", "capacitor_press.glb", [(0, 0, 0)])
    sc.autoplay()
    straight_cols(sc, BASIC_SPEED, BASIC_WALL)
    for sx in (-1, 1):
        sc.box_b(f"Side{'LR'[sx > 0]}", (sx * 0.9, 0, 0.0), (0.16, 1.4, 1.8))
    sc.box_b("Head", (0, 0, 0.85), (1.96, 1.4, 0.3))
    sc.sensor_b("Press", (0, 0, BELT_TOP + 0.4), (1.5, 1.2, 0.7))
    return sc

def belt_scraper():
    sc = room_scene("BeltScraper", "belt_scraper.glb", [(0, 0, 0)])
    sc.autoplay()
    straight_cols(sc, BASIC_SPEED, BASIC_WALL)
    return sc

def rooms():
    out = []
    for fn in (sweep_arm, slot_sieve, terrace_catcher, storm_collector, insulated_belt, capacitor_press, belt_scraper):
        sc = fn()
        out.append(sc.write(D + sc.name + ".tscn"))
    return out
