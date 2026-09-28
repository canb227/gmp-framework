"""
Concept heaters and coolers: three of each, every one built around a different physical mechanism so each poses
its own automation puzzle. Same frame as the other concepts: origin at the anchor cell centre, floor z = -1,
front = +Y, multi-cell pieces grow toward +X / +Y / +Z. Each carries a looping "idle-loop" animation.

  heaters
  concept_tunnel_furnace.glb       1x2x1  belt through a curtained kiln: heat = time inside (belt speed)
                                          in: back at belt height, out: front at belt height           Curtains, Elements, Heat
  concept_magma_bath.glb           2x2x1  open molten pool: drop items in from above; floaters spill over the
                                          front weir, sinkers are dredged out through the side port (heats AND
                                          sorts by density)                                            Crust, Bubbles, Dredge, Pour
  concept_impact_forge.glb         1x1x2  armoured anvil heated by impact energy: items must be *launched* into
                                          its upper window; they drop down a slide out of the same face  Anvil, Flash, Sparks, Meter
  coolers
  concept_quench_tank.glb          1x2x1  drop items into the water from above; a lift belt drags them out
                                          onto a drip-dry belt                                          Lift, Belt, Steam, Ripple, Fan
  concept_spiral_radiator.glb      1x1x3  finned helter-skelter: cooling = ride length; fed from a top hopper,
                                          leaves at the bottom front                                    Fan, Coolant, Core
  concept_counterflow_exchanger.glb 2x2x1 no power: a hot lane and a cold lane run past each other in opposite
                                          directions through a copper wall and trade heat (needs both flows
                                          balanced)                                                     BeltA, BeltB, ChevronsA, ChevronsB
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix

_HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
         else r"C:\Users\steph\OneDrive\Documents\godot\projects\gmp-framework\game\assets\models\machines\concepts\source")
_CON = os.path.join(_HERE, "build_concepts.py")               # the concept helpers (and, through them, salvage_lib)
_S = {"__name__": "concepts_lib", "__file__": _CON}
exec(compile(open(_CON, encoding="utf-8").read(), _CON, "exec"), _S)
_SKIP = {"HERE", "OUT_DIR", "PIECES", "build_all", "ICONS", "export", "CONV"}
globals().update({k: v for k, v in _S.items() if not k.startswith("__") and k not in _SKIP})
HERE = _HERE
OUT_DIR = os.path.dirname(HERE)

C_REFRACTORY = (0.22, 0.11, 0.07)
C_CRUST = (0.1, 0.085, 0.08)
C_ICE = (0.6, 0.85, 1.0)
C_WATER = (0.45, 0.8, 1.0)

def line_path(p0, p1):
    """Straight belt path from p0 to p1 (belt top), level or inclined about X."""
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    t = d.normalized()
    side = t.cross(Vector((0, 0, 1))).normalized() if abs(t.z) < 0.99 else Vector((1, 0, 0))
    up = side.cross(t)
    return Path(d.length, lambda s: (p0 + t * s, t, side, up), 1)

def belt_run(coll, B, rng, p0, p1, name="Belt", guards=True, sides=(-1, 1), cable=True):
    path = line_path(p0, p1)
    build_belt(path, name, coll, path.L)
    for s in sides:
        build_side(B, path, s, rng, guards=guards)
    if cable:
        build_cable(B, path)
    return path

def dial(B, c, n, r=0.1):
    """Round gauge face on a housing, facing n."""
    n = Vector(n)
    B.cyl(c, c + n * 0.02, r, 16, "metal", C_DARK)
    B.cyl(c + n * 0.02, c + n * 0.025, r * 0.85, 16, "panel", (0.85, 0.84, 0.8), 0.1)

def needle(coll, name, c, n, axis, sweep, length=90, r=0.07):
    """Red gauge needle on a dial at c (face normal n), swinging through sweep (radians) about axis."""
    N = Builder()
    u = Vector((0, 0, 1)).cross(Vector(n)).normalized() if abs(Vector(n).z) < 0.9 else Vector((1, 0, 0))
    N.box(u * r * 0.5, (0.01 if abs(u.x) < 0.5 else r, 0.01 if abs(u.y) < 0.5 else r, 0.012), I3, "glow", (1.0, 0.15, 0.1), 0.05)
    ob = node(N, name, coll, c + Vector(n) * 0.03)
    rest = [0, 0, 0]
    swung = [0, 0, 0]; swung[axis] = sweep
    cycle(ob, "rotation_euler", [tuple(rest), tuple(swung)], length)
    return ob

# ====================================================================================== heaters
def build_tunnel_furnace():
    rng = random.Random(1201); random.seed(1201)
    coll = clear_collection("Concept_TunnelFurnace")
    B = Builder()
    belt_run(coll, B, rng, (0, -1, BELT_TOP), (0, 3, BELT_TOP), guards=False)
    y0, y1, top = -0.55, 2.55, 0.75
    for sx in (-1, 1):                                                       # kiln walls: panelled outside, refractory in
        B.box(Vector((sx * 0.93, 1.0, (top - 0.95) / 2)), (0.1, y1 - y0, top + 0.95), I3, "metal", C_FRAME, 0.2, rust=0.3)
        B.box(Vector((sx * 0.87, 1.0, -0.2)), (0.02, y1 - y0 - 0.1, 1.1), I3, "metal", C_REFRACTORY, 0.25)
        panel_face(B, Vector((sx * 0.985, 1.0, -0.05)), (sx, 0, 0), y1 - y0 - 0.2, 1.3, C_WHITE)
        for k in range(4):                                                   # glowing inspection slits
            B.box(Vector((sx * 0.99, 0.0 + k * 0.7, 0.45)), (0.01, 0.4, 0.04), I3, "molten", C_MOLTEN, 0.05)
        hazard(B, Vector((sx * 0.99, y0 + 0.1 if sx < 0 else y1 - 0.1, -0.95)), (0, -sx, 0) if sx > 0 else (0, 1, 0), (0, 0, 1),
               y1 - y0 - 0.2, 0.08, (sx, 0, 0), pitch=0.1)
    B.box(Vector((0, 1.0, top + 0.07)), (1.98, y1 - y0, 0.14), I3, "metal", C_DARK, 0.15)                  # roof
    B.box(Vector((0, 1.0, top + 0.19)), (1.3, y1 - y0 - 0.4, 0.1), I3, "panel", C_WHITE, 0.3)
    for k in range(6):                                                       # roof louvres
        B.box(Vector((0, 0.1 + k * 0.36, top + 0.245)), (1.1, 0.12, 0.02), Matrix.Rotation(0.5, 3, 'X'), "metal", C_DARK, 0.1)
    B.box(Vector((0, 1.0, top - 0.02)), (1.72, y1 - y0 - 0.1, 0.04), I3, "metal", C_REFRACTORY, 0.25)
    for y, n in ((y0, -1), (y1, 1)):                                         # mouth frames
        B.box(Vector((0, y, 0.48)), (1.98, 0.14, 0.4), I3, "metal", C_DARK, 0.15)
        hazard(B, Vector((-0.95 if n < 0 else 0.95, y + n * 0.071, 0.34)), (n * -1 if n > 0 else 1, 0, 0), (0, 0, 1), 1.9, 0.1, (0, n, 0), pitch=0.1)
        B.box(Vector((0, y + n * 0.072, 0.56)), (0.5, 0.004, 0.08), I3, "glow", C_AMBER, 0.05)
    dial(B, Vector((0.99, 2.1, 0.3)), (1, 0, 0))
    B.box(Vector((0.99, 1.4, 0.3)), (0.02, 0.4, 0.2), I3, "metal", C_BLUE, 0.2)
    finish(B, "Frame", coll)
    needle(coll, "Gauge", Vector((0.99, 2.1, 0.3)), (1, 0, 0), 0, 1.4, 120)
    for name, y, poses in (("CurtainIn", y0 - 0.02, [(0.0, 0, 0), (-0.14, 0, 0)]), ("CurtainOut", y1 + 0.02, [(0.1, 0, 0), (-0.03, 0, 0)])):
        Cu = Builder()                                                       # chain curtains keeping the heat in
        for k in range(9):
            x = -0.72 + k * 0.18
            Cu.box(Vector((x, 0, -0.5)), (0.15, 0.012, 1.0), I3, "rubber", (0.12, 0.1, 0.09), 0.15)
            Cu.box(Vector((x, 0, -0.97)), (0.15, 0.02, 0.04), I3, "copper", C_COPPER, 0.15)
        cu = node(Cu, name, coll, Vector((0, y, 0.28)))
        cycle(cu, "rotation_euler", poses, 60)
    El = Builder()                                                           # heating elements across the ceiling
    for k in range(7):
        El.cyl(Vector((-0.82, -0.3 + k * 0.43, 0)), Vector((0.82, -0.3 + k * 0.43, 0)), 0.035, 8, "molten", C_MOLTEN, 0.05)
    el = node(El, "Elements", coll, Vector((0, 0, top - 0.12)))
    cycle(el, "scale", [Vector((1, 1, 1)), Vector((1, 1.02, 1.35))], 40)
    H = Builder()
    H.box(Vector((0, 0, 0)), (1.6, y1 - y0 - 0.1, 1.2), I3, "field_heat", C_MOLTEN, 0.02)
    heat = node(H, "Heat", coll, Vector((0, 1.0, -0.2)))
    cycle(heat, "scale", [Vector((1, 1, 1)), Vector((0.97, 1, 1.06))], 60)
    return coll

def build_magma_bath():
    random.seed(1211)
    coll = clear_collection("Concept_MagmaBath")
    B = Builder()
    cx, cy, rim, lvl = 1.0, 1.0, -0.2, -0.36
    # tank walls (x -0.95..2.95, y -0.95..2.95); the right wall has the dredge port (y -0.9..0.9, z < -0.45),
    # the front wall has the weir notch (x 1.5..2.5, top -0.38)
    wall = lambda lo, hi: B.box((Vector(lo) + Vector(hi)) / 2, tuple(abs(b - a) for a, b in zip(lo, hi)), I3, "metal", C_FRAME, 0.2, rust=0.35)
    wall((-0.95, -0.95, -1), (2.95, -0.83, rim))
    wall((-0.95, -0.95, -1), (-0.83, 2.95, rim))
    wall((2.83, -0.95, -1), (2.95, -0.9, rim)); wall((2.83, 0.9, -1), (2.95, 2.95, rim)); wall((2.83, -0.9, -0.45), (2.95, 0.9, rim))
    wall((-0.95, 2.83, -1), (1.5, 2.95, rim)); wall((2.5, 2.83, -1), (2.95, 2.95, rim)); wall((1.5, 2.83, -1), (2.5, 2.95, -0.4))
    for n, c, w in (((0, -1, 0), (cx, -0.955, -0.6), 3.6), ((-1, 0, 0), (-0.955, cy, -0.6), 3.6), ((0, 1, 0), (0.25, 2.955, -0.6), 2.2)):
        panel_face(B, Vector(c), n, w, 0.6, C_WHITE)
    for (x, y) in ((-0.9, -0.9), (2.9, -0.9), (2.9, 2.9), (-0.9, 2.9)):
        B.box(Vector((x, y, 0.3)), (0.1, 0.1, 1.3), I3, "metal", C_DARK, 0.15)                              # lamp posts
        B.cyl(Vector((x, y, 0.95)), Vector((x, y, 0.98)), 0.06, 10, "glow", C_AMBER, 0.05)
    for (a, b) in (((-0.95, -0.95), (2.95, -0.95)), ((-0.95, -0.95), (-0.95, 2.95))):
        hazard(B, Vector((a[0], a[1] - 0.001, rim - 0.12)) if a[1] == b[1] else Vector((a[0] - 0.001, a[1], rim - 0.12)),
               (1, 0, 0) if a[1] == b[1] else (0, 1, 0), (0, 0, 1), 3.9, 0.1, (0, -1, 0) if a[1] == b[1] else (-1, 0, 0), pitch=0.12)
    B.box(Vector((cx, cy, lvl)), (3.66, 3.66, 0.04), I3, "molten", C_MOLTEN, 0.08)                          # the melt
    for k in range(6):                                                       # burner grilles round the base
        B.box(Vector((-0.2 + k * 0.5, -0.96, -0.85)), (0.3, 0.01, 0.12), I3, "molten", C_MOLTEN, 0.05)
    # weir spout at the front notch and the dredge port on the right
    B.box(Vector((2.0, 2.93, -0.45)), (0.9, 0.3, 0.04), Matrix.Rotation(math.radians(-30), 3, 'X'), "steel", L["C_WEAR"], 0.15)
    for sx in (1.5, 2.5):
        B.box(Vector((sx, 2.9, -0.4)), (0.05, 0.16, 0.2), I3, "panel", C_YELLOW, 0.2)
    B.box(Vector((2.94, 0, -0.42)), (0.04, 1.9, 0.08), I3, "panel", C_YELLOW, 0.2)
    for sy in (-0.95, 0.95):
        B.box(Vector((2.94, sy, -0.7)), (0.04, 0.08, 0.6), I3, "panel", C_YELLOW, 0.2)
    B.box(Vector((1.0, 2.96, -0.1)), (0.6, 0.01, 0.1), I3, "glow", C_AMBER, 0.05)
    finish(B, "Frame", coll)
    Cr = Builder()                                                           # cooled crust rafts drifting on the melt
    rng = random.Random(7)
    for k in range(9):
        a = k / 9 * math.tau + rng.uniform(-0.2, 0.2); r = 0.5 + 1.0 * (k % 3) / 2
        Cr.box(Vector((math.cos(a) * r, math.sin(a) * r, 0)), (rng.uniform(0.25, 0.5), rng.uniform(0.2, 0.4), 0.03),
               Matrix.Rotation(rng.uniform(0, 3), 3, 'Z'), "metal", C_CRUST, 0.3)
    crust = node(Cr, "Crust", coll, Vector((cx, cy, lvl + 0.03)))
    spin(crust, 2, 1, 240)
    for k, (x, y, off) in enumerate(((0.3, 0.6, 2), (1.8, 1.9, 22), (1.2, 0.1, 40), (0.2, 2.2, 55), (2.3, 0.9, 70))):
        Bu = Builder()
        Bu.cyl(Vector((0, 0, -0.03)), Vector((0, 0, 0.05)), 0.12, 12, "molten", (1.0, 0.55, 0.15), 0.05)
        bu = node(Bu, f"Bubble{k}", coll, Vector((x, y, lvl)))
        keys(bu, (1, off, off + 18, off + 24, 121), "scale",
             [Vector((0.01, 0.01, 0.01)), Vector((0.01, 0.01, 0.01)), Vector((1, 1, 1.6)), Vector((0.01, 0.01, 0.01)), Vector((0.01, 0.01, 0.01))])
    Dr = Builder()                                                           # dredge wheel sweeping sinkers out the port
    Dr.cyl(Vector((0, -0.85, 0)), Vector((0, 0.85, 0)), 0.06, 10, "steel", C_STEEL)
    for k in range(6):
        a = k / 6 * math.tau
        Dr.box(Vector((math.cos(a) * 0.2, 0, math.sin(a) * 0.2)), (0.4, 1.6, 0.04), Matrix.Rotation(-a, 3, 'Y'), "steel", (0.3, 0.3, 0.31), 0.2, rust=0.5)
    dr = node(Dr, "Dredge", coll, Vector((2.4, 0, -0.6)))
    spin(dr, 1, -1, 120)
    Po = Builder()                                                           # slag pouring over the weir
    Po.box(Vector((0, 0.1, -0.12)), (0.8, 0.02, 0.26), Matrix.Rotation(math.radians(-30), 3, 'X'), "molten", C_MOLTEN, 0.05)
    po = node(Po, "Pour", coll, Vector((2.0, 2.85, -0.36)))
    cycle(po, "scale", [Vector((1, 1, 1)), Vector((0.85, 1, 1.25))], 30)
    H = Builder()
    H.box(Vector((0, 0, 0)), (3.6, 3.6, 0.5), I3, "field_heat", C_MOLTEN, 0.02)
    heat = node(H, "Heat", coll, Vector((cx, cy, lvl + 0.27)))
    cycle(heat, "scale", [Vector((1, 1, 1)), Vector((0.98, 0.98, 1.08))], 60)
    return coll

def build_impact_forge():
    random.seed(1221)
    coll = clear_collection("Concept_ImpactForge")
    B, G = Builder(), Builder()
    for (x, y) in ((-0.92, -0.92), (0.92, -0.92), (0.92, 0.92), (-0.92, 0.92)):
        B.box(Vector((x, y, 1.0)), (0.12, 0.12, 3.98), I3, "metal", C_FRAME, 0.2, rust=0.3)
    B.box(Vector((0, -0.9, 1.0)), (1.72, 0.1, 3.98), I3, "metal", C_DARK, 0.2)                             # armoured back
    for k in range(5):
        B.box(Vector((0, -0.84, 0.2 + k * 0.55)), (1.72, 0.04, 0.08), I3, "steel", (0.3, 0.3, 0.31), 0.2, rust=0.4)
    B.box(Vector((0, -0.4, 2.93)), (1.98, 1.1, 0.12), I3, "metal", C_DARK, 0.15)                           # roof over the anvil
    B.box(Vector((0, 0.9, 2.93)), (1.98, 0.14, 0.12), I3, "metal", C_DARK, 0.15)                           # window header
    hazard(B, Vector((-0.9, 0.971, 2.88)), (1, 0, 0), (0, 0, 1), 1.8, 0.1, (0, 1, 0), pitch=0.12)
    B.box(Vector((0, 0.9, 0.3)), (1.98, 0.14, 0.12), I3, "metal", C_DARK, 0.15)                            # window sill
    hazard(B, Vector((-0.9, 0.971, 0.25)), (1, 0, 0), (0, 0, 1), 1.8, 0.1, (0, 1, 0), pitch=0.12)
    for sx in (-1, 1):                                                       # aim lamps on the window jambs
        B.box(Vector((sx * 0.86, 0.95, 1.6)), (0.04, 0.04, 0.4), I3, "glow", C_AMBER, 0.05)
    for sx in (-1, 1):                                                       # glass cheeks, frame bands
        G.box(Vector((sx * 0.94, 0, 1.6)), (0.02, 1.7, 2.5), I3, "glass", (0.55, 0.9, 1.0), 0.02)
        B.box(Vector((sx * 0.95, 0, 0.3)), (0.08, 1.9, 0.12), I3, "metal", C_DARK, 0.15)
        panel_face(B, Vector((sx * 0.975, 0, -0.35)), (sx, 0, 0), 1.7, 1.1, C_WHITE)
    # the slide under the anvil to the output mouth at the bottom of the front face
    B.box(Vector((0, 0.1, -0.35)), (1.7, 1.95, 0.04), Matrix.Rotation(math.radians(-24), 3, 'X'), "steel", L["C_WEAR"], 0.15)
    B.box(Vector((0, 0.95, -0.2)), (1.98, 0.1, 0.3), I3, "metal", C_DARK, 0.15)
    for sx in (-1, 1):
        B.box(Vector((sx * 0.8, 0.95, -0.6)), (0.1, 0.1, 0.5), I3, "panel", C_YELLOW, 0.2)
    B.box(Vector((0, 0, -0.97)), (1.98, 1.98, 0.06), I3, "steel", C_STEEL, 0.25, rust=0.6)
    B.box(Vector((0.97, -0.5, 1.4)), (0.04, 0.3, 1.6), I3, "metal", C_BLUE, 0.2)                           # heat meter housing
    finish(B, "Frame", coll); finish(G, "Glass", coll)
    Me = Builder()
    for k in range(10):
        Me.box(Vector((0, 0, k * 0.15 + 0.07)), (0.012, 0.2, 0.11), I3, "molten" if k > 3 else "glow", C_MOLTEN if k > 3 else C_AMBER, 0.05)
    meter = node(Me, "Meter", coll, Vector((0.992, -0.5, 0.65)))
    keys(meter, (1, 30, 34, 90, 91), "scale", [Vector((1, 1, 0.1)), Vector((1, 1, 0.1)), Vector((1, 1, 1)), Vector((1, 1, 0.1)), Vector((1, 1, 0.1))])
    tilt = Matrix.Rotation(math.radians(15), 3, 'X')                         # anvil face leans forward: impacts glance down
    An = Builder()
    An.box(Vector((0, 0, 0)), (1.5, 0.22, 1.5), tilt, "steel", (0.3, 0.3, 0.31), 0.2, rust=0.3)
    An.box(tilt @ Vector((0, 0.115, 0)), (1.3, 0.01, 1.3), tilt, "metal", (0.08, 0.08, 0.08), 0.1)
    for r in (0.55, 0.35, 0.15):
        ring(An, tilt @ Vector((0, 0.12 + (0.6 - r) * 0.01, 0)), tilt @ Vector((0, 1, 0)), r - 0.03, r, 0.01, 28, "panel", C_YELLOW, 0.2)
    for (x, z) in ((-0.55, -0.55), (0.55, -0.55), (0.55, 0.55), (-0.55, 0.55)):                             # damper rams
        An.cyl(tilt @ Vector((x, -0.11, z)), tilt @ Vector((x, -0.3, z)), 0.07, 10, "steel", C_STEEL)
    anvil = node(An, "Anvil", coll, Vector((0, -0.45, 1.5)))
    fr = (1, 30, 33, 60, 91)
    keys(anvil, fr, "location", [Vector((0, -0.45, 1.5)), Vector((0, -0.45, 1.5)), Vector((0, -0.6, 1.49)), Vector((0, -0.45, 1.5)), Vector((0, -0.45, 1.5))])
    Fl = Builder()
    Fl.cyl(Vector((0, 0, 0)), tilt @ Vector((0, 0.01, 0)), 0.45, 24, "molten", C_MOLTEN, 0.05)
    flash = node(Fl, "Flash", coll, tilt @ Vector((0, 0.125, 0)), parent=anvil)
    keys(flash, (1, 30, 33, 70, 91), "scale", [Vector((0.01, 1, 0.01)), Vector((0.01, 1, 0.01)), Vector((1.2, 1, 1.2)), Vector((0.01, 1, 0.01)), Vector((0.01, 1, 0.01))])
    Sp = Builder()
    for k in range(10):
        a = k / 10 * math.tau
        d = Vector((math.cos(a), 0.6, math.sin(a))).normalized()
        Sp.box(d * 0.5, (0.03, 0.2, 0.03), Matrix.Rotation(a, 3, 'Y'), "glow", C_AMBER, 0.05)
    sparks = node(Sp, "Sparks", coll, Vector((0, -0.2, 1.5)))
    keys(sparks, (1, 30, 36, 42, 91), "scale", [Vector((0.01, 0.01, 0.01)), Vector((0.01, 0.01, 0.01)), Vector((1, 1, 1)),
                                                 Vector((0.01, 0.01, 0.01)), Vector((0.01, 0.01, 0.01))])
    return coll

# ====================================================================================== coolers
def build_quench_tank():
    rng = random.Random(1231); random.seed(1231)
    coll = clear_collection("Concept_QuenchTank")
    B, G = Builder(), Builder()
    lift0, lift1 = Vector((0, -0.3, -0.78)), Vector((0, 1.75, 0.2))
    water = -0.1
    for sx in (-1, 1):                                                       # tank sides, running on beside the lift
        B.box(Vector((sx * 0.92, 0.45, -0.38)), (0.1, 2.8, 1.24), I3, "metal", C_FRAME, 0.2, rust=0.3)
        panel_face(B, Vector((sx * 0.975, 0.45, -0.4)), (sx, 0, 0), 2.6, 1.0, C_WHITE)
        G.box(Vector((sx * 0.975, 0.0, -0.35)), (0.004, 1.2, 0.5), I3, "glass", (0.55, 0.9, 1.0), 0.02)    # sight glass
    B.box(Vector((0, -0.92, -0.38)), (1.94, 0.1, 1.24), I3, "metal", C_FRAME, 0.2, rust=0.3)              # back wall
    panel_face(B, Vector((0, -0.975, -0.4)), (0, -1, 0), 1.7, 1.0, C_WHITE)
    B.box(Vector((0, -0.4, -0.95)), (1.84, 1.1, 0.1), I3, "steel", C_STEEL, 0.2, rust=0.5)                 # tank floor
    # under the lift: a sealed plate that is the tank's front wall
    d = lift1 - lift0
    a = math.atan2(d.z, d.y)
    B.box((lift0 + lift1) / 2 + Vector((0, 0.03, -0.12)), (1.84, d.length, 0.04), Matrix.Rotation(a, 3, 'X'), "metal", C_DARK, 0.15)
    B.tube_rings([quad_ring(0, 0, 0.95, 0.92, 0.92), quad_ring(0, 0, 0.35, 0.6, 0.6)], "steel", (0.35, 0.35, 0.36), 0.2, 0.5, cap=False)
    B.tube_rings([quad_ring(0, 0, 0.33, 0.6, 0.6), quad_ring(0, 0, 0.93, 0.94, 0.94)], "steel", (0.35, 0.35, 0.36), 0.2, 0.5, cap=False)
    for (x, y) in ((-0.9, -0.9), (0.9, -0.9), (0.9, 0.9), (-0.9, 0.9)):
        B.box(Vector((x, y, 0.4)), (0.08, 0.08, 1.1), I3, "metal", C_FRAME, 0.2, rust=0.3)
    hazard(B, Vector((-0.9, -0.941, 0.8)), (1, 0, 0), (0, 0, 1), 1.8, 0.1, (0, -1, 0), pitch=0.1)
    # the drip-dry run: deflector down from the lift's head onto a short output belt
    B.box(Vector((0, 2.05, -0.3)), (1.6, 0.9, 0.04), Matrix.Rotation(math.radians(-58), 3, 'X'), "steel", L["C_WEAR"], 0.15)
    for sx in (-1, 1):
        B.box(Vector((sx * 0.9, 2.45, -0.3)), (0.06, 1.1, 0.9), I3, "panel", C_WHITE, 0.3)
    belt_run(coll, B, rng, (0, 2.0, BELT_TOP), (0, 3.0, BELT_TOP), guards=False, cable=False)
    B.box(Vector((0.97, 2.4, 0.25)), (0.06, 0.5, 0.5), I3, "metal", C_BLUE, 0.2)                          # dryer fan housing
    finish(B, "Frame", coll)
    lift = line_path(lift0, lift1)
    build_belt(lift, "Lift", coll, lift.L)
    G.box(Vector((0, 0.11, (water - 0.9) / 2)), (1.72, 2.02, water + 0.9), I3, "glass", C_WATER, 0.02)   # the water
    finish(G, "Glass", coll)
    Ri = Builder()
    ring(Ri, Vector((0, 0, 0)), (0, 0, 1), 0.2, 0.24, 0.005, 24, "cyan", C_CYAN, 0.05)
    ripple = node(Ri, "Ripple", coll, Vector((0, -0.2, water + 0.005)))
    keys(ripple, (1, 60, 61), "scale", [Vector((0.2, 0.2, 1)), Vector((2.8, 2.8, 1)), Vector((0.2, 0.2, 1))], linear=True)
    for k, (x, y, off) in enumerate(((-0.4, -0.5, 1), (0.3, 0.2, 20), (0.1, -0.7, 40))):
        St = Builder()
        St.box(Vector((0, 0, 0)), (0.3, 0.3, 0.3), I3, "field_cold", C_ICE, 0.02)
        st = node(St, f"Steam{k}", coll, Vector((x, y, water)))
        keys(st, (1, off, off + 40, off + 41, 61 + 40), "location",
             [Vector((x, y, water)), Vector((x, y, water)), Vector((x, y, 0.8)), Vector((x, y, water)), Vector((x, y, water))], linear=True)
        keys(st, (1, off, off + 10, off + 40, 61 + 40), "scale",
             [Vector((0.01, 0.01, 0.01)), Vector((0.01, 0.01, 0.01)), Vector((1, 1, 1)), Vector((0.01, 0.01, 0.01)), Vector((0.01, 0.01, 0.01))])
    Fn = Builder()
    for k in range(5):
        a = k / 5 * math.tau
        Fn.box(Vector((0, math.cos(a) * 0.1, math.sin(a) * 0.1)), (0.01, 0.16, 0.05), Matrix.Rotation(a, 3, 'X') @ Matrix.Rotation(0.4, 3, 'Y'), "metal", (0.2, 0.2, 0.21), 0.1)
    fn = node(Fn, "Fan", coll, Vector((0.935, 2.4, 0.25)))
    spin(fn, 0, 4, 60)
    return coll

SR_TOP, SR_BOT, SR_TURNS = 4.3, -0.88, 2.25          # spiral radiator slide: top (back, angle -90), bottom (angle 0)
SR_R0, SR_R1 = 0.3, 0.86
def build_spiral_radiator():
    random.seed(1241)
    coll = clear_collection("Concept_SpiralRadiator")
    B = Builder()
    for (x, y) in ((-0.92, -0.92), (0.92, -0.92), (0.92, 0.92), (-0.92, 0.92)):
        B.box(Vector((x, y, 2.0)), (0.1, 0.1, 5.98), I3, "metal", C_FRAME, 0.2, rust=0.3)
    for z in (-0.95, 1.3, 3.6):
        for sx in (-1, 1):
            B.box(Vector((sx * 0.92, 0, z)), (0.08, 1.84, 0.08), I3, "metal", C_FRAME, 0.2, rust=0.3)
        B.box(Vector((0, -0.92, z)), (1.84, 0.08, 0.08), I3, "metal", C_FRAME, 0.2, rust=0.3)
    B.box(Vector((0, 0, -0.97)), (1.98, 1.98, 0.06), I3, "steel", C_STEEL, 0.25, rust=0.6)
    B.cyl(Vector((0, 0, -1)), Vector((0, 0, 4.7)), SR_R0 - 0.02, 20, "metal", C_DARK, 0.15)                 # chilled core
    for k in range(34):                                                      # radiator fins down the core
        z = -0.7 + k * 0.16
        ring(B, Vector((0, 0, z)), (0, 0, 1), SR_R0 - 0.02, SR_R0 + 0.005, 0.02, 20, "copper", C_COPPER, 0.15)
    helix(B, SR_BOT, SR_TOP, -SR_TURNS, SR_R0, SR_R1, 0.03, 180, "steel", (0.5, 0.52, 0.55))              # the slide
    helix(B, SR_BOT, SR_TOP, -SR_TURNS, SR_R1 - 0.03, SR_R1 + 0.02, 0.16, 180, "panel", C_WHITE)          # outer lip
    helix(B, SR_BOT + 0.031, SR_TOP + 0.031, -SR_TURNS, SR_R0 + 0.02, SR_R0 + 0.05, 0.004, 180, "cyan", C_CYAN)
    for k in range(20):                                                      # outer radiator fins, open at the front mouth
        a = k / 20 * math.tau
        if abs(math.atan2(math.sin(a - math.pi / 2), math.cos(a - math.pi / 2))) < 0.5:
            continue
        B.box(Vector((math.cos(a) * 0.94, math.sin(a) * 0.94, 1.9)), (0.06, 0.012, 4.8), Matrix.Rotation(a, 3, 'Z'), "copper", C_COPPER, 0.15, rust=0.2)
    # run-out at the bottom: from the slide's end (x ~ 0.58, y 0) forward to the front face, guided to the middle
    B.box(Vector((0.3, 0.5, -0.87)), (1.2, 1.0, 0.04), I3, "steel", L["C_WEAR"], 0.15)
    B.box(Vector((0.62, 0.62, -0.72)), (0.04, 0.9, 0.3), Matrix.Rotation(math.radians(30), 3, 'Z'), "panel", C_YELLOW, 0.2)
    hazard(B, Vector((-0.9, 0.971, -0.95)), (1, 0, 0), (0, 0, 1), 1.8, 0.08, (0, 1, 0), pitch=0.1)
    # feed hopper on top, over the slide's head at the back
    B.tube_rings([quad_ring(0, -0.55, 4.98, 0.42, 0.42), quad_ring(0, -0.55, 4.45, 0.26, 0.26)], "steel", (0.35, 0.35, 0.36), 0.2, 0.5, cap=False)
    B.tube_rings([quad_ring(0, -0.55, 4.43, 0.26, 0.26), quad_ring(0, -0.55, 4.96, 0.44, 0.44)], "steel", (0.35, 0.35, 0.36), 0.2, 0.5, cap=False)
    B.box(Vector((0, 0.4, 4.8)), (1.84, 0.9, 0.12), I3, "panel", C_FACILITY, 0.1)                         # cap with the fan
    B.cyl(Vector((0, 0.4, 4.86)), Vector((0, 0.4, 4.9)), 0.38, 24, "metal", C_DARK)
    B.pipe([Vector((-0.8, -0.8, 4.7)), Vector((-0.8, -0.8, -0.7))], 0.05, 8, "glass", (0.55, 0.9, 1.0))    # coolant riser
    finish(B, "Frame", coll)
    Fn = Builder()
    for k in range(6):
        a = k / 6 * math.tau
        Fn.box(Vector((math.cos(a) * 0.17, math.sin(a) * 0.17, 0)), (0.28, 0.08, 0.01), Matrix.Rotation(a, 3, 'Z') @ Matrix.Rotation(0.4, 3, 'X'), "metal", (0.2, 0.2, 0.21), 0.1)
    fan = node(Fn, "Fan", coll, Vector((0, 0.4, 4.92)))
    spin(fan, 2, 3, 60)
    for k in range(3):
        Co = Builder()
        Co.cyl(Vector((0, 0, -0.12)), Vector((0, 0, 0.12)), 0.045, 8, "cyan", C_CYAN, 0.05)
        co = node(Co, f"Coolant{k}", coll, Vector((-0.8, -0.8, 4.6)))
        o = 1 + k * 40                                                       # blobs sink down the riser, 120 frames apart / 3
        z_at = lambda f: 4.6 - ((f - o) % 120) / 120 * 5.2
        if o > 1:                                                            # wrap back to the top at frame o
            keys(co, (1, o - 1, o, 121), "location", [Vector((-0.8, -0.8, z_at(1))), Vector((-0.8, -0.8, z_at(o - 1))),
                                                    Vector((-0.8, -0.8, 4.6)), Vector((-0.8, -0.8, z_at(121)))], linear=True)
        else:
            keys(co, (1, 121), "location", [Vector((-0.8, -0.8, 4.6)), Vector((-0.8, -0.8, -0.6))], linear=True)
    Cr = Builder()
    Cr.cyl(Vector((0, 0, 0)), Vector((0, 0, 5.4)), SR_R0 - 0.015, 20, "field_cold", C_ICE, 0.02)
    core = node(Cr, "Core", coll, Vector((0, 0, -0.85)))
    cycle(core, "scale", [Vector((1, 1, 1)), Vector((1.08, 1.08, 1))], 60)
    return coll

def build_counterflow_exchanger():
    rng = random.Random(1251); random.seed(1251)
    coll = clear_collection("Concept_CounterflowExchanger")
    B, G = Builder(), Builder()
    belt_run(coll, B, rng, (0, -1, BELT_TOP), (0, 3, BELT_TOP), name="BeltA", sides=(-1,))       # hot lane, toward +Y
    belt_run(coll, B, rng, (2, 3, BELT_TOP), (2, -1, BELT_TOP), name="BeltB", sides=(-1,))       # cold lane, toward -Y
    pa, pb = line_path((0, -1, BELT_TOP), (0, 3, BELT_TOP)), line_path((2, 3, BELT_TOP), (2, -1, BELT_TOP))
    build_side(B, pa, 1, rng, guards=False); build_side(B, pb, 1, rng, guards=False)
    # the copper heat wall between the lanes
    B.box(Vector((1.0, 1.0, -0.35)), (0.12, 3.9, 1.1), I3, "copper", C_COPPER, 0.15, rust=0.15)
    for k in range(32):
        y = -0.85 + k * 0.12
        B.box(Vector((1.0, y, -0.3)), (0.22, 0.02, 0.95), I3, "copper", (0.85, 0.45, 0.2), 0.15)
    B.box(Vector((1.0, 1.0, 0.28)), (0.3, 3.96, 0.12), I3, "metal", C_DARK, 0.15)                           # manifold
    B.cyl(Vector((1.0, -0.98, 0.42)), Vector((1.0, 2.98, 0.42)), 0.08, 12, "glass", (0.55, 0.9, 1.0))
    for y in (-0.7, 1.0, 2.7):                                               # canopy arches over both lanes
        B.box(Vector((1.0, y, 0.75)), (3.9, 0.1, 0.08), I3, "metal", C_DARK, 0.15)
        for x in (-0.95, 2.95):
            B.box(Vector((x, y, -0.1)), (0.08, 0.1, 1.7), I3, "metal", C_DARK, 0.15)
    G.box(Vector((1.0, 1.0, 0.78)), (3.8, 3.9, 0.02), I3, "glass", (0.55, 0.9, 1.0), 0.02)
    for x in (-0.94, 2.94):
        G.box(Vector((x, 1.0, 0.1)), (0.02, 3.9, 1.3), I3, "glass", (0.55, 0.9, 1.0), 0.02)
    # lane lamps: hot in / cool out on lane A, cold in / warm out on lane B
    for (x, y, c) in ((0, -0.99, C_MOLTEN), (0, 2.99, C_CYAN), (2, 2.99, C_CYAN), (2, -0.99, C_MOLTEN)):
        B.box(Vector((x, y, 0.62)), (1.2, 0.1, 0.18), I3, "metal", C_DARK, 0.15)
        B.box(Vector((x, y + (0.051 if y > 0 else -0.051), 0.62)), (0.9, 0.004, 0.08), I3, "molten" if c == C_MOLTEN else "cyan", c, 0.05)
    finish(B, "Frame", coll); finish(G, "Glass", coll)
    for name, x, sgn, mat, col in (("ChevronsA", 0.88, 1, "molten", C_MOLTEN), ("ChevronsB", 1.12, -1, "cyan", C_CYAN)):
        Ch = Builder()
        for k in range(9):
            y = -0.6 + k * 0.4
            for s in (-1, 1):
                Ch.box(Vector((0, y - sgn * 0.07, 0.07 * s)), (0.01, 0.2, 0.035), Matrix.Rotation(-sgn * s * 0.785, 3, 'X'), mat, col, 0.05)
        ch = node(Ch, name, coll, Vector((x, 0.8, 0.0)))
        keys(ch, (1, 31), "location", [Vector((x, 0.8, 0.0)), Vector((x, 0.8 + sgn * 0.4, 0.0))], linear=True)
    return coll

PIECES = [
    (build_tunnel_furnace, "concept_tunnel_furnace.glb"),
    (build_magma_bath, "concept_magma_bath.glb"),
    (build_impact_forge, "concept_impact_forge.glb"),
    (build_quench_tank, "concept_quench_tank.glb"),
    (build_spiral_radiator, "concept_spiral_radiator.glb"),
    (build_counterflow_exchanger, "concept_counterflow_exchanger.glb"),
]
ICONS = []

def build_all(do_export=True):
    return build_family(PIECES, OUT_DIR, do_export)

if __name__ == "__main__":
    build_all(do_export=False)
