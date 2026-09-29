"""
Concept structures: proof-of-concept models (with looping "idle-loop" animations) for physics-driven machines.
Origin at the anchor cell centre, floor z = -1, front = +Y, as every other family.

  concept_gravity_inverter.glb      1x1x1  floor plate that flips gravity above it     Orbits, Field
  concept_tag_gate.glb              1x1x1  belt through a gate that opens per item tag  Belt, DoorL, DoorR
  concept_bounce_pad.glb            1x1x1  tunable rubber spring pad                    Pad, Springs, Dial
  concept_vortex_funnel.glb         2x2x1  spinning bowl, drops items out of its centre Bowl
  concept_tube_straight/bend/junction/receiver.glb  1x1x1 pneumatic tube network     Pulse, Flap, Bellows
  concept_heat_lamp.glb             1x1x1  belt under a heating lamp (tags HOT)          Belt, Cone, Coil
  concept_cryo_vent.glb             1x1x1  belt under a cold vent (tags COLD)            Belt, Cone, Fan
  concept_counterweight_elevator.glb 1x1x3 two cages on one cable over a pulley          CageA, CageB, Pulley
  concept_rail_gun.glb              1x4x1  metal-only coil launcher                      Sled, Charge
  concept_tipping_bucket.glb        1x1x2  seesaw bucket that alternates left / right    Bucket
  concept_assembly_chamber.glb      3x3x3  zero-g assembly cube (anchored on its centre)  Field, Parts, Emitters
  concept_screw_elevator.glb        1x1x3  Archimedes screw in a glass tube              Screw
  concept_platform_elevator.glb     1x2x3  paternoster: level platforms on a chain loop  Platform0-5, Sprockets
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix

_HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
         else r"C:\Users\steph\OneDrive\Documents\godot\projects\gmp-framework\game\assets\models\machines\concepts\source")
_LIB = os.path.normpath(os.path.join(_HERE, "..", "..", "..", "shared", "salvage_lib.py"))
_S = {"__name__": "salvage_lib", "__file__": _LIB}
exec(compile(open(_LIB, encoding="utf-8").read(), _LIB, "exec"), _S)
_SKIP = {"HERE", "OUT_DIR", "PIECES", "build_all", "ICONS", "export", "CONV"}
globals().update({k: v for k, v in _S.items() if not k.startswith("__") and k not in _SKIP})
HERE = _HERE
OUT_DIR = os.path.dirname(HERE)

# ---------- detail helpers (shared with build_thermal.py, which execs this file) ----------
UNLIT = {"glow", "cyan", "molten", "violet", "lamp", "glass"}

class Builder(Builder):
    """The salvage Builder, but boxes stay sharp where a chamfer can't read at game distance (glowing strips,
    sheet thinner than 2 cm, parts smaller than 8 cm), which keeps the tri count for the detail that does."""
    min_chamfer, min_size = 0.004, 0.08

    def box(self, center, size, basis=Matrix.Identity(3), mat="metal", c=C_FRAME, var=0.18, rust=0.0, bevel=None):
        if bevel is None and (mat in UNLIT or mat.startswith("field") or max(size) < self.min_size
                              or min(self.bevel, 0.2 * min(size)) < self.min_chamfer):
            bevel = 0
        return super().box(center, size, basis, mat, c, var, rust, bevel)

def slab(B, center, size, basis=I3, mat="metal", c=C_FRAME, var=0.18, rust=0.0, bevel=None):
    """A box standing on the floor: same as B.box but without its (never seen) bottom face."""
    fs = B.box(center, size, basis, mat, c, var, rust, bevel)
    for f in fs:
        f.normal_update()
    low = min(fs, key=lambda f: (f.normal.z, f.calc_center_median().z))
    bmesh.ops.delete(B.bm, geom=[low], context='FACES_ONLY')
    return [f for f in fs if f is not low]

def stud(B, p, n, r=0.013, h=0.012, seg=6, mat="steel", c=C_STEEL, rust=0.4):
    """Bolt head / rivet: a short open-bottomed prism standing on the surface at p along n (16 tris)."""
    n = Vector(n).normalized(); R = rot_to(n)
    pts = [R @ Vector((math.cos(a) * r, math.sin(a) * r, 0)) for a in [(k + 0.5) / seg * math.tau for k in range(seg)]]
    lo = [B.bm.verts.new(p + q) for q in pts]
    hi = [B.bm.verts.new(p + n * h + q * 0.85) for q in pts]
    fs = [B.quad([lo[k], lo[(k + 1) % seg], hi[(k + 1) % seg], hi[k]], mat) for k in range(seg)]
    fs.append(B.quad(hi, mat))
    bmesh.ops.recalc_face_normals(B.bm, faces=fs)
    B.paint(fs, c, 0.15, rust)
    return fs

def bolt_circle(B, c, axis, r, n, br=0.014, h=0.012, phase=0.0, rust=0.4):
    """n bolt heads on a circle of radius r round axis, standing out of the face at c."""
    R = rot_to(axis)
    for k in range(n):
        a = phase + k / n * math.tau
        stud(B, c + R @ Vector((math.cos(a) * r, math.sin(a) * r, 0)), axis, br, h, rust=rust)

def bolt_row(B, p0, p1, n, normal, br=0.012, h=0.01, rust=0.4):
    """n bolt heads evenly from p0 to p1 (inclusive) on a face with the given normal."""
    for k in range(n):
        stud(B, p0.lerp(p1, k / max(1, n - 1)), normal, br, h, rust=rust)

def decal(B, c, n, w, h, mat, col, var=0.15, rust=0.0, up=None, lift=0.002):
    """One flat quad (w across, h up) lying just off a surface at c facing n: seams, streaks, labels."""
    R = facing_basis(n) if up is None else Matrix((Vector(up).cross(Vector(n)).normalized(), Vector(up).normalized(), Vector(n).normalized())).transposed()
    c = Vector(c) + Vector(n).normalized() * lift
    vs = [B.bm.verts.new(c + R @ Vector((x * w / 2, y * h / 2, 0))) for x, y in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    f = B.quad(vs, mat)
    f.normal_update()
    if f.normal.dot(Vector(n)) < 0:
        f.normal_flip()
    B.paint([f], col, var, rust)
    return f

def streak(B, c, n, w=0.03, l=0.25, mat="metal"):
    """Rust run-off under a bolt or seam: a narrow quad hanging down from c on the face n."""
    return decal(B, Vector(c) - Vector((0, 0, l / 2)), n, w, l, mat, (0.27, 0.13, 0.06), 0.35, 0.6, lift=0.0015)

def seam(B, p0, p1, n, w=0.01, mat="metal", col=(0.05, 0.05, 0.055)):
    """Dark panel-joint line from p0 to p1 on the face n."""
    d = Vector(p1) - Vector(p0)
    return decal(B, (Vector(p0) + Vector(p1)) / 2, n, w, d.length, mat, col, 0.1, up=d)

def warn_plate(B, c, n, s=0.14):
    """Yellow warning sign with a black triangle (6 tris, panel material)."""
    decal(B, c, n, s, s * 0.8, "panel", C_YELLOW, 0.2, lift=0.003)
    R = facing_basis(n); o = Vector(c) + Vector(n).normalized() * 0.0045
    for k, (sc, col) in enumerate(((0.3, C_BLACK), (0.17, C_YELLOW))):
        o2 = o + Vector(n).normalized() * 0.0008 * k
        tri = [B.bm.verts.new(o2 + R @ Vector((x * s * sc, y * s * sc - s * 0.03, 0))) for x, y in ((-1, -0.7), (1, -0.7), (0, 1))]
        f = B.bm.faces.new(tri); f.material_index = MI["panel"]; f.normal_update()
        if f.normal.dot(Vector(n)) < 0:
            f.normal_flip()
        B.paint([f], col, 0.1)

def grille(B, c, n, w, h, slats=5, mat="metal", col=C_DARK):
    """Louvred vent: a dark recess quad with angled slats (single quads) across it."""
    decal(B, c, n, w, h, mat, (0.02, 0.02, 0.022), 0.05, lift=0.002)
    R = facing_basis(n); nn = Vector(n).normalized()
    for k in range(slats):
        y = -h / 2 + (k + 0.5) * h / slats
        a = [Vector(c) + R @ Vector((x * w / 2, y - h / slats * 0.35, 0)) + nn * 0.004 for x in (-1, 1)]
        b = [Vector(c) + R @ Vector((x * w / 2, y + h / slats * 0.35, 0)) + nn * 0.014 for x in (-1, 1)]
        f = B.quad([B.bm.verts.new(v) for v in (a[0], a[1], b[1], b[0])], mat)
        f.normal_update()
        if f.normal.dot(nn + Vector((0, 0, 1)) * 0.2) < 0:
            f.normal_flip()
        B.paint([f], col, 0.15)

def cable(B, pts, r=0.014, clips=(), col=(0.03, 0.03, 0.032), mat="panel"):
    """Cable through pts with a clamp box at each point index in clips (panel material by default: a black sheath
    that costs no extra surface on a frame that has no rubber on it already)."""
    pts = [Vector(p) for p in pts]
    B.pipe(pts, r, 6, mat, col)
    for i in clips:
        d = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        B.box(pts[i], (r * 3.2, r * 3.2, r * 3.2), rot_to(d), "steel", C_STEEL, 0.2, rust=0.3)

def foot(B, x, y, post=0.1, s=0.22, z=-1.0):
    """Base plate under a corner post at (x, y), flush with the post's outer faces (so it stays inside the cell),
    bolted down at the three corners the post doesn't cover."""
    sx, sy = math.copysign(1, x), math.copysign(1, y)
    c = Vector((x + sx * (post - s) / 2, y + sy * (post - s) / 2, z + 0.012))
    slab(B, c, (s, s, 0.024), I3, "steel", C_STEEL, 0.2, rust=0.5)
    o = s / 2 - 0.03
    for (a, b) in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
        if (a, b) != (sx, sy):
            stud(B, c + Vector((a * o, b * o, 0.012)), (0, 0, 1), 0.014, 0.014)

def gusset(B, corner, u, v, s=0.16, t=0.012, col=C_FRAME):
    """Triangular corner plate in the plane of u and v at a frame joint (both faces, 4 tris)."""
    corner, u, v = Vector(corner), Vector(u).normalized(), Vector(v).normalized()
    n = u.cross(v).normalized()
    fs = []
    for sgn in (-1, 1):
        o = corner + n * sgn * t / 2
        f = B.bm.faces.new([B.bm.verts.new(o), B.bm.verts.new(o + u * s), B.bm.verts.new(o + v * s)])
        f.material_index = MI["metal"]; f.normal_update()
        if f.normal.dot(n * sgn) < 0:
            f.normal_flip()
        fs.append(f)
    B.paint(fs, col, 0.2, 0.3)

def motor(B, c, axis, r=0.14, l=0.3, col=C_BLUE):
    """Motor can lying along axis: ribbed body, end bell, junction box and a flange bolt ring."""
    ax = Vector(axis).normalized(); c = Vector(c)
    p0, p1 = c - ax * l / 2, c + ax * l / 2
    B.cyl(p0, p1, r, 12, "metal", col, 0.2, rust=0.1)
    for k in range(4):
        ring(B, p0.lerp(p1, 0.2 + k * 0.2), ax, r, r + 0.012, 0.02, 12, "metal", col, 0.2)
    B.cyl(p1, p1 + ax * 0.04, r * 0.75, 12, "metal", C_DARK, 0.15)
    bolt_circle(B, p0, -ax, r * 0.8, 4, 0.012, 0.01, phase=0.4)
    up = Vector((0, 0, 1)) if abs(ax.z) < 0.9 else Vector((1, 0, 0))
    B.box(c + up * (r + 0.03), (0.09, 0.09, 0.06) if abs(ax.z) < 0.9 else (0.06, 0.09, 0.09), I3, "metal", C_DARK, 0.15)

def orient(fs, toward):
    """Flip faces of an open surface so each normal points along toward(face centre) (single-sided in Godot)."""
    for f in fs:
        f.normal_update()
        if f.normal.dot(toward(f.calc_center_median())) < 0:
            f.normal_flip()

def disc_ring(c, r, n=24, axis='Z'):
    pts = []
    for k in range(n):
        a = k / n * math.tau
        v = {'Z': Vector((math.cos(a) * r, math.sin(a) * r, 0)), 'Y': Vector((math.cos(a) * r, 0, math.sin(a) * r)),
             'X': Vector((0, math.cos(a) * r, math.sin(a) * r))}[axis]
        pts.append(c + v)
    return pts

def glass_tube(B, p0, p1, r, n=24):
    ax = (p1 - p0).normalized()
    ref = Vector((0, 0, 1)) if abs(ax.z) < 0.9 else Vector((1, 0, 0))
    u = ax.cross(ref).normalized(); w = ax.cross(u)
    ring_ = lambda p: [p + (u * math.cos(k / n * math.tau) + w * math.sin(k / n * math.tau)) * r for k in range(n)]
    B.tube_rings([ring_(p0), ring_(p1)], "glass", (0.55, 0.9, 1.0), 0.02, 0.0, cap=False, smooth=True)

def helix(B, z0, z1, turns, r0, r1, thick, steps, mat, c, phase=0.0):
    """Screw flight: a thin helical ribbon between radii r0 and r1."""
    bm = B.bm
    rows = []
    for i in range(steps + 1):
        t = i / steps
        a = phase + t * turns * math.tau
        z = z0 + (z1 - z0) * t
        d = Vector((math.cos(a), math.sin(a), 0))
        rows.append([bm.verts.new(d * r + Vector((0, 0, z + dz))) for r, dz in ((r0, 0), (r1, 0), (r1, thick), (r0, thick))])
    fs = []
    for i in range(steps):
        a, b = rows[i], rows[i + 1]
        for k in range(4):
            f = bm.faces.new((a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k])); f.material_index = MI[mat]; f.smooth = True
            fs.append(f)
    bmesh.ops.recalc_face_normals(bm, faces=fs)
    B.paint(fs, c, 0.15)

def belt_cell(coll, rng, B, guards=True):
    path = PATHS["straight"]
    build_belt(path, "Belt", coll, 2.0)
    for side in (-1, 1):
        build_side(B, path, side, rng, guards=guards)
    build_cable(B, path)
    return path

# ======================================================================================
def build_gravity_inverter():
    rng = random.Random(901); random.seed(901)
    coll = clear_collection("Concept_GravityInverter")
    B = Builder()
    slab(B, Vector((0, 0, -0.95)), (1.94, 1.94, 0.1), I3, "metal", C_FRAME, 0.2, rust=0.3, bevel=0.025)
    B.box(Vector((0, 0, -0.895)), (1.5, 1.5, 0.02), I3, "metal", (0.05, 0.05, 0.06), 0.1)
    for n in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, -1, 0))):   # plate edge: vents and bolts
        R = facing_basis(n)
        for k in (-1, 1):
            grille(B, n * 0.97 + R @ Vector((k * 0.35, 0, 0)) + Vector((0, 0, -0.95)), n, 0.4, 0.05, 3)
        for k in range(8):
            t = -0.72 + k * (1.44 / 7)
            stud(B, n * 0.87 + R @ Vector((t, 0, 0)) + Vector((0, 0, -0.9)), (0, 0, 1), 0.014, 0.01)
    for (x, y) in ((-0.82, -0.82), (0.82, -0.82), (0.82, 0.82), (-0.82, 0.82)):                  # pillar flanges
        B.cyl(Vector((x, y, -0.9)), Vector((x, y, -0.88)), 0.12, 12, "metal", C_DARK, 0.15)
        bolt_circle(B, Vector((x, y, -0.88)), (0, 0, 1), 0.095, 4, 0.012, 0.01, phase=0.785)
        streak(B, Vector((x + (0.12 if x > 0 else -0.12) * 0.707, y + (0.12 if y > 0 else -0.12) * 0.707, -0.9)), Vector((x, y, 0)).normalized(), 0.04, 0.06)
    cable(B, [Vector((0.82, -0.72, -0.62)), Vector((0.82, -0.66, -0.885)), Vector((0.2, -0.94, -0.885)),
              Vector((-0.72, -0.94, -0.885)), Vector((-0.82, -0.9, -0.84))], 0.012, clips=(2, 3))
    for k in range(5):
        o = -0.6 + k * 0.3
        B.box(Vector((o, 0, -0.883)), (0.02, 1.4, 0.004), I3, "violet", C_VIOLET, 0.05)
        B.box(Vector((0, o, -0.883)), (1.4, 0.02, 0.004), I3, "violet", C_VIOLET, 0.05)
    for (org, u, n) in ((Vector((-0.85, -0.92, -0.9)), (1, 0, 0), (0, -1, 0)), (Vector((0.85, 0.92, -0.9)), (-1, 0, 0), (0, 1, 0)),
                        (Vector((0.92, -0.85, -0.9)), (0, 1, 0), (1, 0, 0)), (Vector((-0.92, 0.85, -0.9)), (0, -1, 0), (-1, 0, 0))):
        hazard(B, org, u, (0, 0, 1), 1.7, 0.08, n, pitch=0.1)
    for (x, y) in ((-0.82, -0.82), (0.82, -0.82), (0.82, 0.82), (-0.82, 0.82)):
        B.cyl(Vector((x, y, -0.9)), Vector((x, y, -0.15)), 0.07, 12, "metal", C_DARK, 0.15)
        B.cyl(Vector((x, y, -0.7)), Vector((x, y, -0.35)), 0.075, 12, "panel", C_WHITE, 0.3)
        ring(B, Vector((x, y, -0.2)), (0, 0, 1), 0.07, 0.1, 0.04, 12, "violet", C_VIOLET, 0.05)
        B.cyl(Vector((x, y, -0.15)), Vector((x, y, -0.1)), 0.05, 12, "violet", C_VIOLET, 0.05)
    for s in (-1, 1):                                                        # "up" chevrons painted on the plate
        vprism(B, [(-0.25, s * 0.25 - 0.15), (0.25, s * 0.25 - 0.15), (0.0, s * 0.25 + 0.15)], -0.886, -0.88, "panel", C_YELLOW, 0.2)
    cb = Vector((0.82, -0.82, -0.55))
    B.box(cb + Vector((0, -0.1, 0)), (0.14, 0.06, 0.18), I3, "panel", C_WHITE, 0.3)
    B.box(cb + Vector((0, -0.132, 0.04)), (0.05, 0.004, 0.03), I3, "violet", C_VIOLET, 0.05)
    warn_plate(B, cb + Vector((0, -0.131, -0.04)), (0, -1, 0), 0.08)
    finish(B, "Frame", coll)
    O = Builder()
    for k in range(4):
        a = k / 4 * math.tau
        p = Vector((math.cos(a) * 0.55, math.sin(a) * 0.55, 0))
        rock(O, p, 0.06, 40 + k, 1, 0.1, "violet", lambda q, n: C_VIOLET)
    orb = node(O, "Orbits", coll, Vector((0, 0, -0.45)))
    spin(orb, 2, 1, 90)
    cycle(orb, "location", [Vector((0, 0, -0.45)), Vector((0, 0, 0.35))], 90)
    F = Builder()
    F.box(Vector((0, 0, 0.9)), (1.8, 1.8, 1.8), I3, "field_ag", C_VIOLET, 0.02)
    fld = node(F, "Field", coll, Vector((0, 0, -0.9)))
    cycle(fld, "scale", [Vector((1, 1, 1)), Vector((0.96, 0.96, 1.04))], 90)
    return coll

# ======================================================================================
def build_tag_gate():
    rng = random.Random(911); random.seed(911)
    coll = clear_collection("Concept_TagGate")
    B = Builder()
    belt_cell(coll, rng, B)
    top = 0.45
    for sx in (-1, 1):
        B.box(Vector((sx * 0.93, 0, (BELT_TOP + top) / 2)), (0.12, 0.22, top - BELT_TOP), I3, "metal", C_DARK, 0.15)
        panel_face(B, Vector((sx * 0.93, -0.112, -0.2)), (0, -1, 0), 0.1, 1.0, C_WHITE, seam=False)
        B.box(Vector((sx * 0.865, 0, -0.2)), (0.01, 0.12, 1.1), I3, "cyan", C_CYAN, 0.05)          # detector strips
        for y in (-0.112, 0.112):                                                                    # bolted mounting brackets
            B.box(Vector((sx * 0.93, y * 1.08, BELT_TOP + 0.09)), (0.16, 0.012, 0.14), I3, "steel", C_STEEL, 0.2, rust=0.5)
            for z in (BELT_TOP + 0.05, BELT_TOP + 0.13):
                stud(B, Vector((sx * 0.9, y * 1.08 + (0.006 if y > 0 else -0.006), z)), (0, 1 if y > 0 else -1, 0), 0.01, 0.008)
        grille(B, Vector((sx * 0.991, 0, 0.1)), (sx, 0, 0), 0.14, 0.4, 6)                          # sensor cooling vents
        seam(B, Vector((sx * 0.991, -0.1, -0.45)), Vector((sx * 0.991, 0.1, -0.45)), (sx, 0, 0))
        streak(B, Vector((sx * 0.991, 0.06, -0.12)), (sx, 0, 0), 0.025, 0.3)
    cable(B, [Vector((0.95, 0.1, BELT_TOP + 0.1)), Vector((0.95, 0.128, -0.3)), Vector((0.95, 0.128, 0.3)), Vector((0.95, 0.1, top + 0.02))],
          0.014, clips=(1, 2))
    B.box(Vector((0, 0, top + 0.08)), (1.98, 0.24, 0.16), I3, "metal", C_DARK, 0.15)
    panel_face(B, Vector((0, -0.121, top + 0.08)), (0, -1, 0), 1.7, 0.14, C_WHITE, seam=False)
    for sx in (-1, 1):
        bolt_row(B, Vector((sx * 0.9, -0.121, top + 0.03)), Vector((sx * 0.9, -0.121, top + 0.13)), 2, (0, -1, 0), 0.01, 0.008)
        bolt_row(B, Vector((sx * 0.9, 0.121, top + 0.03)), Vector((sx * 0.9, 0.121, top + 0.13)), 2, (0, 1, 0), 0.01, 0.008)
    grille(B, Vector((0, 0.121, top + 0.08)), (0, 1, 0), 0.8, 0.1, 3)                              # back of the header
    warn_plate(B, Vector((0.62, 0.121, top + 0.08)), (0, 1, 0), 0.12)
    for x in (-0.6, 0.0, 0.6):
        seam(B, Vector((x, -0.1, top + 0.161)), Vector((x, 0.1, top + 0.161)), (0, 0, 1))
    for k, col in enumerate((C_AMBER, C_CYAN, (1.0, 0.15, 0.1), C_COPPER)):                         # tag lamps
        B.box(Vector((-0.45 + k * 0.3, -0.125, top + 0.08)), (0.16, 0.01, 0.1), I3, "glow" if k != 1 else "cyan", col, 0.05)
    B.box(Vector((0, 0.0, top - 0.02)), (1.6, 0.06, 0.03), I3, "cyan", C_CYAN, 0.05)                 # scan bar
    finish(B, "Frame", coll)
    for name, sx in (("DoorL", -1), ("DoorR", 1)):
        D = Builder()
        D.box(Vector((-sx * 0.42, 0, 0.45)), (0.84, 0.04, 0.9), I3, "panel", C_WHITE, 0.3)
        for y in (-1, 1):                                                    # window pane and frame, both faces
            D.box(Vector((-sx * 0.42, y * 0.021, 0.55)), (0.5, 0.004, 0.25), I3, "glass", (0.55, 0.9, 1.0), 0.02)
            for (dx, dz, w, h) in ((0, 0.14, 0.56, 0.03), (0, -0.14, 0.56, 0.03), (0.265, 0, 0.03, 0.25), (-0.265, 0, 0.03, 0.25)):
                D.box(Vector((-sx * 0.42 + dx, y * 0.024, 0.55 + dz)), (w, 0.008, h), I3, "metal", C_DARK, 0.15)
        hazard(D, Vector((-sx * 0.84 if sx > 0 else 0.0, -0.021, 0.02)), (1, 0, 0), (0, 0, 1), 0.84, 0.1, (0, -1, 0), pitch=0.08)
        seam(D, Vector((-sx * 0.02, -0.021, 0.3)), Vector((-sx * 0.82, -0.021, 0.3)), (0, -1, 0))
        decal(D, Vector((-sx * 0.7, -0.021, 0.2)), (0, -1, 0), 0.1, 0.05, "panel", C_BLACK, 0.1)       # push plate
        D.cyl(Vector((0, 0, 0)), Vector((0, 0, 0.92)), 0.025, 8, "steel", C_STEEL)
        for z in (0.12, 0.78):                                               # hinge knuckles
            D.cyl(Vector((0, 0, z - 0.05)), Vector((0, 0, z + 0.05)), 0.034, 8, "steel", C_STEEL, rust=0.4)
        door = node(D, name, coll, Vector((sx * 0.86, 0, BELT_TOP + 0.02)))
        keys(door, (1, 30, 42, 90, 102, 121), "rotation_euler",
             [(0, 0, 0), (0, 0, 0), (0, 0, -sx * math.radians(80)), (0, 0, -sx * math.radians(80)), (0, 0, 0), (0, 0, 0)])
    return coll

# ======================================================================================
def build_bounce_pad():
    random.seed(921)
    coll = clear_collection("Concept_BouncePad")
    B = Builder()
    slab(B, Vector((0, 0, -0.8)), (1.9, 1.9, 0.4), I3, "metal", C_FRAME, 0.2, rust=0.3, bevel=0.03)
    for n in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, -1, 0))):
        panel_face(B, n * 0.955 + Vector((0, 0, -0.8)), n, 1.6, 0.3, C_WHITE)
        R = facing_basis(n)
        for sx in (-1, 1):                                                   # panel corner screws
            for sz in (-1, 1):
                stud(B, n * 0.973 + R @ Vector((sx * 0.76, sz * 0.12, 0)) + Vector((0, 0, -0.8)), n, 0.011, 0.006)
        if n.y <= 0:
            grille(B, n * 0.974 + R @ Vector((-0.35 if n.y < 0 else 0.0, -0.02, 0)) + Vector((0, 0, -0.8)), n, 0.5, 0.14, 4)
    hazard(B, Vector((-0.8, -0.96, -0.98)), (1, 0, 0), (0, 0, 1), 1.6, 0.06, (0, -1, 0), pitch=0.08)
    B.box(Vector((0, 0, -0.6)), (1.7, 1.7, 0.02), I3, "steel", C_STEEL, 0.2, rust=0.4)
    for (x, y) in ((-0.55, -0.55), (0.55, -0.55), (0.55, 0.55), (-0.55, 0.55)):                  # spring seats
        B.cyl(Vector((x, y, -0.59)), Vector((x, y, -0.575)), 0.16, 12, "metal", C_DARK, 0.15)
        bolt_circle(B, Vector((x, y, -0.575)), (0, 0, 1), 0.145, 4, 0.011, 0.008, phase=0.785)
    for (x, y) in ((-0.8, 0), (0.8, 0), (0, -0.8), (0, 0.8)):                                     # rubber end stops
        B.box(Vector((x, y, -0.55)), (0.12, 0.12, 0.08), I3, "panel", (0.03, 0.03, 0.03), 0.1)
    dial = Vector((0.7, -0.96, -0.78))
    B.cyl(dial, dial + Vector((0, -0.02, 0)), 0.1, 16, "metal", C_DARK)
    B.cyl(dial + Vector((0, -0.02, 0)), dial + Vector((0, -0.025, 0)), 0.085, 16, "panel", (0.85, 0.84, 0.8), 0.1)
    bolt_circle(B, dial + Vector((0, -0.02, 0)), (0, -1, 0), 0.093, 4, 0.007, 0.005, phase=0.785)
    warn_plate(B, Vector((0.35, -0.974, -0.72)), (0, -1, 0), 0.12)
    finish(B, "Frame", coll)
    Sp = Builder()
    for (x, y) in ((-0.55, -0.55), (0.55, -0.55), (0.55, 0.55), (-0.55, 0.55)):
        n = 5 * 10                                                           # five coils, ten samples a turn
        pts = [Vector((x + math.cos(t) * 0.12, y + math.sin(t) * 0.12, t / (5 * math.tau) * 0.25))
               for t in [i / n * 5 * math.tau for i in range(n + 1)]]
        Sp.pipe(pts, 0.018, 6, "steel", (0.55, 0.55, 0.55))
    springs = node(Sp, "Springs", coll, Vector((0, 0, -0.59)))
    P = Builder()
    P.box(Vector((0, 0, 0)), (1.7, 1.7, 0.06), I3, "steel", C_STEEL, 0.2, rust=0.3)
    P.box(Vector((0, 0, 0.06)), (1.6, 1.6, 0.06), I3, "rubber", C_BLACK, 0.1)
    for (org, u, n) in ((Vector((-0.85, -0.851, -0.03)), (1, 0, 0), (0, -1, 0)), (Vector((0.85, 0.851, -0.03)), (-1, 0, 0), (0, 1, 0))):
        hazard(P, org, u, (0, 0, 1), 1.7, 0.06, n, pitch=0.08)
    P.box(Vector((0, 0, 0.092)), (0.5, 0.5, 0.004), I3, "panel", C_YELLOW, 0.2)
    for x in (-0.7, -0.5, -0.32, 0.32, 0.5, 0.7):                            # tread grooves in the rubber
        decal(P, Vector((x, 0, 0.09)), (0, 0, 1), 0.02, 1.5, "rubber", (0.045, 0.045, 0.045), 0.1, up=(0, 1, 0))
    for sx in (-1, 1):
        for sy in (-1, 1):
            stud(P, Vector((sx * 0.82, sy * 0.82, 0.03)), (0, 0, 1), 0.014, 0.01)
    pad = node(P, "Pad", coll, Vector((0, 0, -0.3)))
    fr = (1, 40, 46, 56, 66, 91)
    keys(pad, fr, "location", [Vector((0, 0, -0.3)), Vector((0, 0, -0.38)), Vector((0, 0, -0.14)), Vector((0, 0, -0.33)),
                                Vector((0, 0, -0.29)), Vector((0, 0, -0.3))])
    keys(springs, fr, "scale", [Vector((1, 1, 1)), Vector((1, 1, 0.7)), Vector((1, 1, 1.65)), Vector((1, 1, 0.88)),
                                 Vector((1, 1, 1.04)), Vector((1, 1, 1))])
    N = Builder()
    N.box(Vector((0.035, 0, 0)), (0.07, 0.004, 0.01), I3, "glow", (1.0, 0.15, 0.1), 0.05)
    needle = node(N, "Dial", coll, dial + Vector((0, -0.03, 0)))
    cycle(needle, "rotation_euler", [(0, 0.4, 0), (0, -1.2, 0)], 90)
    return coll

# ======================================================================================
def build_vortex():
    random.seed(931)
    coll = clear_collection("Concept_VortexFunnel")
    C_ = Vector((1, 1, 0))
    B = Builder()
    corners = ((-0.9, -0.9), (2.9, -0.9), (2.9, 2.9), (-0.9, 2.9))
    for (x, y) in corners:
        B.box(Vector((x, y, -0.1)), (0.12, 0.12, 1.8), I3, "metal", C_FRAME, 0.2, rust=0.3)
        foot(B, x, y, 0.12)
    ring(B, C_ + Vector((0, 0, 0.85)), (0, 0, 1), 1.88, 1.97, 0.1, 64, "metal", C_DARK, 0.2)
    ring(B, C_ + Vector((0, 0, 0.9)), (0, 0, 1), 1.9, 1.95, 0.02, 64, "cyan", C_CYAN, 0.05)
    bolt_circle(B, C_ + Vector((0, 0, 0.9)), (0, 0, 1), 1.935, 24, 0.014, 0.012, phase=0.13)
    for i, (a, b) in enumerate(zip(corners, corners[1:] + corners[:1])):
        B.box(Vector(((a[0] + b[0]) / 2, (a[1] + b[1]) / 2, 0.82)), (abs(b[0] - a[0]) + 0.12, abs(b[1] - a[1]) + 0.12, 0.08), I3, "metal", C_DARK, 0.2)
        a3, b3 = Vector((a[0], a[1], -0.85)), Vector((b[0], b[1], 0.76))       # one diagonal brace per side, low rail too
        if i % 2:
            a3, b3 = Vector((a[0], a[1], 0.76)), Vector((b[0], b[1], -0.85))
        B.cyl(a3, b3, 0.03, 6, "steel", C_STEEL, rust=0.4)
        B.box(Vector(((a[0] + b[0]) / 2, (a[1] + b[1]) / 2, -0.8)), (abs(b[0] - a[0]) or 0.08, abs(b[1] - a[1]) or 0.08, 0.08), I3, "metal", C_FRAME, 0.2, rust=0.3)
        for (x, y) in (a, b):                                                # gussets under the top rail
            d = Vector((b[0] - a[0], b[1] - a[1], 0)).normalized() * (1 if (x, y) == a else -1)
            gusset(B, Vector((x, y, 0.78)) + d * 0.06, d, (0, 0, -1), 0.2)
    B.cyl(C_ + Vector((0, 0, -1.0)), C_ + Vector((0, 0, -0.45)), 0.42, 20, "metal", C_DARK, 0.15)            # outlet sleeve
    B.cyl(C_ + Vector((0, 0, -1.0)), C_ + Vector((0, 0, -0.96)), 0.52, 20, "steel", C_STEEL, 0.2, rust=0.5)
    bolt_circle(B, C_ + Vector((0, 0, -0.96)), (0, 0, 1), 0.47, 8, 0.014, 0.012)
    ring(B, C_ + Vector((0, 0, -0.5)), (0, 0, 1), 0.42, 0.46, 0.06, 20, "metal", C_DARK, 0.15)
    motor(B, C_ + Vector((1.0, 0, -0.72)), (1, 0, 0), 0.17, 0.4)                                        # drive motor
    B.box(C_ + Vector((1.0, 0, -0.93)), (0.46, 0.36, 0.1), I3, "metal", C_FRAME, 0.2, rust=0.3)
    B.box(C_ + Vector((0.62, 0, -0.62)), (0.55, 0.1, 0.14), I3, "metal", C_BLUE, 0.2)                 # belt guard to the sleeve
    B.pipe([C_ + Vector((0.75, 0, -0.7)), C_ + Vector((0.45, 0, -0.55))], 0.05, 8, "steel", C_STEEL)
    cable(B, [C_ + Vector((1.0, 0.1, -0.53)), C_ + Vector((1.2, 0.5, -0.9)), Vector((2.81, 0.6, -0.9)), Vector((2.81, -0.8, -0.3)),
              Vector((2.81, -0.82, 0.7))], 0.016, clips=(2, 3))
    warn_plate(B, Vector((1.0, -0.961, -0.75)), (0, -1, 0), 0.16)
    B.box(Vector((1.0, -0.92, -0.75)), (0.2, 0.06, 0.2), I3, "metal", C_FRAME, 0.2)
    hazard(B, Vector((-0.8, -0.961, -0.98)), (1, 0, 0), (0, 0, 1), 3.6, 0.08, (0, -1, 0), pitch=0.12)
    finish(B, "Frame", coll)
    Bw = Builder()
    rs = [(1.85, 0.85), (1.7, 0.72), (1.3, 0.35), (0.85, -0.05), (0.45, -0.4)]
    Bw.tube_rings([disc_ring(Vector((0, 0, z)), r, 48) for r, z in rs], "steel", L["C_WEAR"], 0.12, 0.1, cap=False, smooth=True)
    Bw.tube_rings([disc_ring(Vector((0, 0, z - 0.07)), r + 0.03, 48) for r, z in rs][::-1], "metal", C_DARK, 0.12, 0.1, cap=False, smooth=True)
    for k in range(12):                                                      # welded seams between the bowl's pressed segments
        a = k / 12 * math.tau
        Bw.pipe([Vector((math.cos(a) * r, math.sin(a) * r, z + 0.012)) for r, z in rs[:4]], 0.008, 4, "steel", (0.3, 0.3, 0.3))
    for k in range(6):                                                       # spiral vanes
        pts = []
        for i in range(9):
            t = i / 8
            r = 1.8 - 1.3 * t
            a = k / 6 * math.tau + t * 2.2
            z = 0.84 - 1.2 * t - 0.02 + 0.03
            pts.append(Vector((math.cos(a) * r, math.sin(a) * r, max(z, -0.37 + 0.03))))
        Bw.pipe(pts, 0.03, 5, "panel", C_YELLOW if k % 2 else C_DARK)
    ring(Bw, Vector((0, 0, -0.4)), (0, 0, 1), 0.42, 0.48, 0.05, 24, "cyan", C_CYAN, 0.05)
    bowl = node(Bw, "Bowl", coll, C_)
    spin(bowl, 2, 1, 60)
    return coll

# ======================================================================================
TUBE_R = 0.72
def tube_collars(B, pts_dirs):
    for p, d in pts_dirs:
        d = Vector(d)
        ring(B, p, d, TUBE_R, TUBE_R + 0.12, 0.12, 32, "metal", C_DARK, 0.15, rust=0.2)
        ring(B, p + d * 0.07, d, TUBE_R + 0.01, TUBE_R + 0.06, 0.02, 32, "cyan", C_CYAN, 0.05)
        inward = -d if p.dot(d) > 0 else d
        ring(B, p + inward * 0.075, d, TUBE_R + 0.005, TUBE_R + 0.15, 0.03, 32, "metal", C_DARK, 0.15, rust=0.3)   # flange
        bolt_circle(B, p + inward * 0.09, inward, TUBE_R + 0.11, 10, 0.014, 0.012, phase=0.2)

def tube_leg(B, p, along=(0, 1, 0)):
    B.box(Vector((p.x, p.y, (-1 + p.z - TUBE_R) / 2)), (0.12, 0.12, p.z - TUBE_R + 1), I3, "metal", C_FRAME, 0.2, rust=0.3)
    B.box(Vector((p.x, p.y, p.z - TUBE_R - 0.02)), (1.4, 0.16, 0.06), I3, "metal", C_DARK, 0.2)
    slab(B, Vector((p.x, p.y, -0.99)), (0.5, 0.4, 0.02), I3, "steel", C_STEEL, 0.2, rust=0.5)
    for sx in (-1, 1):
        for sy in (-1, 1):
            stud(B, Vector((p.x + sx * 0.2, p.y + sy * 0.15, -0.98)), (0, 0, 1), 0.016, 0.014)
        gusset(B, Vector((p.x + sx * 0.06, p.y, p.z - TUBE_R - 0.05)), (sx, 0, 0), (0, 0, -1), 0.2)   # saddle brackets
        gusset(B, Vector((p.x + sx * 0.06, p.y, -0.98)), (sx, 0, 0), (0, 0, 1), 0.16)
        for sy in (-1, 1):
            stud(B, Vector((p.x + sx * 0.62, p.y + sy * 0.081, p.z - TUBE_R - 0.02)), (0, sy, 0), 0.011, 0.008)
    warn_plate(B, Vector((p.x, p.y - 0.061, -0.55)), (0, -1, 0), 0.09)

def pulse_node(coll, name, path_pts, length=30):
    """A glowing ring that races along the tube (location + orientation keyed along path_pts)."""
    P = Builder()
    ring(P, Vector((0, 0, 0)), (0, 1, 0), TUBE_R - 0.08, TUBE_R - 0.02, 0.05, 28, "cyan", C_CYAN, 0.05)
    ob = node(P, name, coll, path_pts[0][0])
    frames = [1 + round(length * i / (len(path_pts) - 1)) for i in range(len(path_pts))]
    keys(ob, frames, "location", [p for p, r in path_pts], linear=True)
    keys(ob, frames, "rotation_euler", [r for p, r in path_pts], linear=True)
    return ob

def booster_band(B, c, d):
    """Seams and rivets on the white booster band round a tube."""
    d = Vector(d)
    R = rot_to(d)
    for k in range(8):
        a = (k + 0.5) / 8 * math.tau
        o = R @ Vector((math.cos(a), math.sin(a), 0))
        for s in (-1, 1):
            stud(B, c + o * (TUBE_R + 0.1) + d * s * 0.11, o, 0.01, 0.006, rust=0.2)
    for s in (-1, 1):
        ring(B, c + d * s * 0.15, d, TUBE_R + 0.005, TUBE_R + 0.115, 0.012, 32, "metal", C_DARK, 0.15)

TUBE_Z = 0.0
def build_tube_straight():
    random.seed(941)
    coll = clear_collection("Concept_TubeStraight")
    B, G = Builder(), Builder()
    glass_tube(G, Vector((0, -0.98, TUBE_Z)), Vector((0, 0.98, TUBE_Z)), TUBE_R)
    tube_collars(B, [(Vector((0, -0.92, TUBE_Z)), (0, 1, 0)), (Vector((0, 0.92, TUBE_Z)), (0, 1, 0))])
    ring(B, Vector((0, 0, TUBE_Z)), (0, 1, 0), TUBE_R, TUBE_R + 0.1, 0.3, 32, "panel", C_WHITE, 0.3)       # booster band
    B.box(Vector((0, 0, TUBE_Z + TUBE_R + 0.14)), (0.3, 0.24, 0.14), I3, "metal", C_BLUE, 0.2)
    B.box(Vector((0.08, -0.13, TUBE_Z + TUBE_R + 0.16)), (0.05, 0.004, 0.03), I3, "cyan", C_CYAN, 0.05)
    grille(B, Vector((-0.06, -0.121, TUBE_Z + TUBE_R + 0.14)), (0, -1, 0), 0.12, 0.08, 3)
    booster_band(B, Vector((0, 0, TUBE_Z)), (0, 1, 0))
    cable(B, [Vector((0.15, 0.05, TUBE_Z + TUBE_R + 0.12)), Vector((0.3, 0.1, TUBE_Z + TUBE_R + 0.02)),
              Vector((0.1, 0.07, TUBE_Z - TUBE_R - 0.1)), Vector((0.07, 0.07, -0.6)), Vector((0.07, 0.07, -0.97))], 0.012, clips=(3,))
    tube_leg(B, Vector((0, 0, TUBE_Z)))
    finish(B, "Frame", coll); finish(G, "Glass", coll)
    pulse_node(coll, "Pulse", [(Vector((0, -0.9, TUBE_Z)), (0, 0, 0)), (Vector((0, 0.9, TUBE_Z)), (0, 0, 0))])
    return coll

def build_tube_bend():
    random.seed(943)
    coll = clear_collection("Concept_TubeBend")
    B, G = Builder(), Builder()
    piv = Vector((1, -1, TUBE_Z))
    n = 12
    rings_ = []
    for i in range(n + 1):
        th = math.pi / 2 * i / n
        c = piv + Vector((-math.cos(th), math.sin(th), 0))
        t = Vector((math.sin(th), math.cos(th), 0))
        u = Vector((0, 0, 1)); w = t.cross(u)
        rings_.append([c + (w * math.cos(k / 24 * math.tau) + u * math.sin(k / 24 * math.tau)) * TUBE_R for k in range(24)])
    G.tube_rings(rings_, "glass", (0.55, 0.9, 1.0), 0.02, 0.0, cap=False, smooth=True)
    tube_collars(B, [(Vector((0, -0.92, TUBE_Z)), (0, 1, 0)), (Vector((0.92, 0, TUBE_Z)), (1, 0, 0))])
    mid = piv + Vector((-math.cos(math.pi / 4), math.sin(math.pi / 4), 0))
    ring(B, mid, (1, 1, 0), TUBE_R, TUBE_R + 0.1, 0.25, 32, "panel", C_WHITE, 0.3)
    booster_band(B, mid, Vector((1, 1, 0)).normalized())
    tube_leg(B, mid)
    finish(B, "Frame", coll); finish(G, "Glass", coll)
    pts = []
    for i in range(7):
        th = math.pi / 2 * i / 6
        pts.append((piv + Vector((-math.cos(th), math.sin(th), 0)), (0, 0, -th)))
    pulse_node(coll, "Pulse", pts)
    return coll

def build_tube_junction():
    random.seed(945)
    coll = clear_collection("Concept_TubeJunction")
    B, G = Builder(), Builder()
    glass_tube(G, Vector((0, -0.98, TUBE_Z)), Vector((0, 0.98, TUBE_Z)), TUBE_R)
    glass_tube(G, Vector((0.3, 0, TUBE_Z)), Vector((0.98, 0, TUBE_Z)), TUBE_R * 0.95)
    tube_collars(B, [(Vector((0, -0.92, TUBE_Z)), (0, 1, 0)), (Vector((0, 0.92, TUBE_Z)), (0, 1, 0)), (Vector((0.92, 0, TUBE_Z)), (1, 0, 0))])
    B.box(Vector((0, 0, TUBE_Z + TUBE_R + 0.12)), (0.6, 0.6, 0.18), I3, "metal", C_DARK, 0.15)              # switch housing
    panel_face(B, Vector((0, -0.301, TUBE_Z + TUBE_R + 0.12)), (0, -1, 0), 0.5, 0.14, C_WHITE, seam=False)
    B.box(Vector((0.15, -0.305, TUBE_Z + TUBE_R + 0.12)), (0.06, 0.004, 0.04), I3, "glow", C_AMBER, 0.05)
    B.box(Vector((-0.15, -0.305, TUBE_Z + TUBE_R + 0.12)), (0.06, 0.004, 0.04), I3, "cyan", C_CYAN, 0.05)
    B.cyl(Vector((0, 0, TUBE_Z + TUBE_R)), Vector((0, 0, TUBE_Z + TUBE_R + 0.05)), 0.05, 10, "steel", C_STEEL)
    for sx in (-1, 1):
        for sy in (-1, 1):
            stud(B, Vector((sx * 0.26, sy * 0.26, TUBE_Z + TUBE_R + 0.21)), (0, 0, 1), 0.014, 0.01)
    grille(B, Vector((0, 0.301, TUBE_Z + TUBE_R + 0.12)), (0, 1, 0), 0.4, 0.12, 4)
    warn_plate(B, Vector((0.301, 0.0, TUBE_Z + TUBE_R + 0.12)), (1, 0, 0), 0.12)
    cable(B, [Vector((-0.3, 0.1, TUBE_Z + TUBE_R + 0.1)), Vector((-0.42, 0.12, TUBE_Z + TUBE_R - 0.05)),
              Vector((-0.3, 0.07, TUBE_Z - TUBE_R - 0.1)), Vector((-0.27, 0.07, -0.6)), Vector((-0.27, 0.07, -0.97))], 0.012, clips=(3,))
    tube_leg(B, Vector((-0.2, 0, TUBE_Z)))
    finish(B, "Frame", coll); finish(G, "Glass", coll)
    Fl = Builder()                                                           # diverter flap, hinged at the centre
    Fl.box(Vector((0, 0.35, 0)), (0.04, 0.7, 1.2), I3, "panel", C_WHITE, 0.3)
    Fl.box(Vector((0.021, 0.35, 0)), (0.004, 0.6, 0.12), I3, "panel", C_YELLOW, 0.2)
    Fl.cyl(Vector((0, 0, -0.6)), Vector((0, 0, 0.6)), 0.03, 8, "steel", C_STEEL)
    flap = node(Fl, "Flap", coll, Vector((0, 0, TUBE_Z)))
    keys(flap, (1, 30, 40, 80, 90, 121), "rotation_euler",
         [(0, 0, 0), (0, 0, 0), (0, 0, -0.75), (0, 0, -0.75), (0, 0, 0), (0, 0, 0)])
    pulse_node(coll, "Pulse", [(Vector((0, -0.9, TUBE_Z)), (0, 0, 0)), (Vector((0, 0.9, TUBE_Z)), (0, 0, 0))])
    return coll

def build_tube_receiver():
    random.seed(947)
    coll = clear_collection("Concept_TubeReceiver")
    B, G = Builder(), Builder()
    glass_tube(G, Vector((0, -0.98, TUBE_Z)), Vector((0, -0.25, TUBE_Z)), TUBE_R)
    tube_collars(B, [(Vector((0, -0.92, TUBE_Z)), (0, 1, 0))])
    # hood the tube empties into, dropping items down a slide onto a belt in front at belt height
    B.box(Vector((0, 0.1, -0.025)), (1.8, 0.7, 1.95), I3, "metal", C_FRAME, 0.2, rust=0.3)
    for n in (Vector((1, 0, 0)), Vector((-1, 0, 0))):
        panel_face(B, Vector((n.x * 0.905, 0.1, 0.1)), n, 0.6, 1.5, C_WHITE)
    B.box(Vector((0, 0.5, -0.6)), (1.4, 0.04, 0.5), I3, "metal", (0.02, 0.02, 0.02), 0.05)                 # mouth
    for sx in (-1, 1):
        B.box(Vector((sx * 0.74, 0.52, -0.55)), (0.08, 0.08, 0.66), I3, "steel", C_CYAN, 0.1)
    B.box(Vector((0, 0.52, -0.2)), (1.56, 0.08, 0.08), I3, "steel", C_CYAN, 0.1)
    B.box(Vector((0, 0.75, -0.83)), (1.4, 0.5, 0.03), Matrix.Rotation(math.radians(-12), 3, 'X'), "steel", L["C_WEAR"], 0.15)
    hazard(B, Vector((-0.9, 0.451, 0.95)), (1, 0, 0), (0, 0, 1), 1.8, 0.1, (0, 1, 0), pitch=0.1)
    for sx in (-1, 1):                                                       # hood: corner bolts, vents, run-off
        for z in (-0.85, -0.3, 0.3, 0.8):
            for y in (-0.22, 0.42):
                stud(B, Vector((sx * 0.9, y, z)), (sx, 0, 0), 0.012, 0.008)
        grille(B, Vector((sx * 0.923, 0.1, -0.55)), (sx, 0, 0), 0.4, 0.3, 5)
        streak(B, Vector((sx * 0.901, 0.42, 0.78)), (sx, 0, 0), 0.03, 0.35)
    ring(B, Vector((0, -0.25, TUBE_Z)), (0, 1, 0), TUBE_R, TUBE_R + 0.14, 0.06, 32, "metal", C_DARK, 0.15, rust=0.3)
    bolt_circle(B, Vector((0, -0.28, TUBE_Z)), (0, -1, 0), TUBE_R + 0.1, 10, 0.014, 0.012)
    warn_plate(B, Vector((0.55, 0.451, 0.5)), (0, 1, 0), 0.16)
    B.box(Vector((-0.55, 0.47, 0.45)), (0.3, 0.04, 0.4), I3, "panel", C_WHITE, 0.3)                        # control box
    B.box(Vector((-0.6, 0.492, 0.5)), (0.06, 0.004, 0.04), I3, "cyan", C_CYAN, 0.05)
    B.box(Vector((-0.5, 0.492, 0.5)), (0.06, 0.004, 0.04), I3, "panel", C_YELLOW, 0.05)
    cable(B, [Vector((-0.55, 0.48, 0.25)), Vector((-0.8, 0.5, 0.0)), Vector((-0.85, 0.47, -0.95))], 0.012, clips=(1,))
    finish(B, "Frame", coll); finish(G, "Glass", coll)
    Be = Builder()                                                           # bellows on top: the pump
    for k in range(5):
        Be.box(Vector((0, 0, k * 0.07)), (0.7 - (k % 2) * 0.08, 0.5 - (k % 2) * 0.06, 0.05), I3, "rubber", C_BLACK, 0.1)
    Be.box(Vector((0, 0, 0.36)), (0.74, 0.54, 0.05), I3, "metal", C_DARK, 0.15)
    bel = node(Be, "Bellows", coll, Vector((0, 0.1, 0.95)))
    cycle(bel, "scale", [Vector((1, 1, 1)), Vector((1, 1, 0.55))], 40)
    return coll

# ======================================================================================
def emitter_cell(name, kind):
    rng = random.Random(951 if kind == "heat" else 953); random.seed(rng.random())
    coll = clear_collection(name)
    B = Builder()
    belt_cell(coll, rng, B)
    for sx in (-1, 1):
        B.box(Vector((sx * 0.96, 0, (BELT_TOP + 0.62) / 2)), (0.07, 0.14, 0.62 - BELT_TOP), I3, "metal", C_DARK, 0.15)
        for sy in (-1, 1):                                                   # bolted brackets on the stringer, header gussets
            B.box(Vector((sx * 0.96, sy * 0.1, BELT_TOP + 0.08)), (0.07, 0.06, 0.16), I3, "steel", C_STEEL, 0.2, rust=0.5)
            stud(B, Vector((sx * 0.96, sy * 0.13, BELT_TOP + 0.08)), (0, sy, 0), 0.01, 0.008)
            gusset(B, Vector((sx * 0.96, sy * 0.05, 0.57)), (-sx, 0, 0), (0, 0, -1), 0.14)
        streak(B, Vector((sx * 0.996, 0, 0.3)), (sx, 0, 0), 0.03, 0.4)
    B.box(Vector((0, 0, 0.62)), (1.98, 0.16, 0.1), I3, "metal", C_DARK, 0.15)
    hazard(B, Vector((-0.99, -0.081, 0.58)), (1, 0, 0), (0, 0, 1), 1.98, 0.06, (0, -1, 0), pitch=0.1)
    bolt_row(B, Vector((-0.85, 0.081, 0.62)), Vector((0.85, 0.081, 0.62)), 7, (0, 1, 0), 0.011, 0.008)
    head = Vector((0, 0, 0.42))
    hood = lambda z, r: disc_ring(head + Vector((0, 0, z)), r, 32)
    if kind == "heat":
        inner = B.tube_rings([hood(0.15, 0.2), hood(0.05, 0.32), hood(-0.1, 0.42)], "steel", (0.7, 0.68, 0.62), 0.1, 0.0, cap=False, smooth=True)
        orient(inner, lambda p: head - p)                                    # reflector faces in (seen from below)
        shell = B.tube_rings([hood(-0.1, 0.42), hood(-0.08, 0.44), hood(0.06, 0.34), hood(0.15, 0.2)], "metal", C_DARK, 0.1, 0.2, cap=False, smooth=True)
        orient(shell, lambda p: p - head + Vector((0, 0, 0.3)))             # outer shell faces out
        ring(B, head + Vector((0, 0, -0.1)), (0, 0, 1), 0.41, 0.45, 0.025, 32, "metal", C_DARK, 0.15, rust=0.3)
        B.cyl(head + Vector((0, 0, 0.15)), head + Vector((0, 0, 0.22)), 0.2, 20, "metal", C_DARK, 0.15)
        for k in range(8):                                                   # cooling fins on the lamp cap
            a = k / 8 * math.tau
            B.box(head + Vector((math.cos(a) * 0.2, math.sin(a) * 0.2, 0.17)), (0.1, 0.012, 0.1), Matrix.Rotation(a, 3, 'Z'), "metal", C_DARK, 0.1)
        B.box(head + Vector((0, 0, 0.2)), (0.5, 0.1, 0.1), I3, "metal", C_DARK, 0.15)
        warn_plate(B, head + Vector((0.12, -0.051, 0.2)), (0, -1, 0), 0.08)
        cable(B, [head + Vector((0.2, 0, 0.2)), Vector((0.6, 0, 0.64)), Vector((0.9, 0, 0.66)), Vector((0.935, -0.1, 0.3)),
                  Vector((0.935, -0.1, -0.4)), Vector((0.97, -0.02, BELT_TOP + 0.2))], 0.02, clips=(3, 4))
        tank = None
    else:
        B.box(head + Vector((0, 0, 0.05)), (0.7, 0.5, 0.3), I3, "panel", C_WHITE, 0.3)
        B.box(head + Vector((0, 0, -0.11)), (0.62, 0.42, 0.02), I3, "metal", (0.08, 0.1, 0.12), 0.1)
        for k in range(5):
            B.box(head + Vector((-0.24 + k * 0.12, 0, -0.125)), (0.03, 0.4, 0.012), I3, "cyan", (0.6, 0.85, 1.0), 0.05)
        for sy in (-1, 1):                                                   # side intakes, corner screws, frost on the lip
            grille(B, head + Vector((0.12, sy * 0.25, 0.06)), (0, sy, 0), 0.34, 0.16, 5)
            for sx in (-1, 1):
                stud(B, head + Vector((sx * 0.31, sy * 0.25, 0.16)), (0, sy, 0), 0.01, 0.006)
            decal(B, head + Vector((0, sy * 0.25, -0.07)), (0, sy, 0), 0.66, 0.05, "panel", (0.85, 0.93, 0.98), 0.1)
        seam(B, head + Vector((-0.35, -0.1, 0.201)), head + Vector((0.35, -0.1, 0.201)), (0, 0, 1))
        warn_plate(B, head + Vector((-0.2, -0.251, 0.06)), (0, -1, 0), 0.1)
        tank = Vector((-0.75, 0.0, 0.3))
        B.cyl(tank, tank + Vector((0, 0, 0.28)), 0.1, 14, "panel", (0.75, 0.8, 0.85), 0.1)
        B.cyl(tank + Vector((0, 0, 0.28)), tank + Vector((0, 0, 0.31)), 0.04, 8, "steel", C_STEEL)
        ring(B, tank + Vector((0, 0, 0.14)), (0, 0, 1), 0.1, 0.105, 0.03, 14, "cyan", C_CYAN, 0.05)
        for z in (0.04, 0.24):                                               # tank straps
            ring(B, tank + Vector((0, 0, z)), (0, 0, 1), 0.1, 0.108, 0.02, 14, "metal", C_DARK, 0.1)
        B.box(tank + Vector((-0.12, 0, 0.14)), (0.1, 0.06, 0.24), I3, "steel", C_STEEL, 0.2, rust=0.4)
        B.pipe([tank + Vector((0.05, 0, 0.28)), head + Vector((-0.2, 0, 0.3)), head + Vector((-0.2, 0, 0.2))], 0.02, 6, "steel", C_STEEL)
        cable(B, [head + Vector((0.3, 0.1, 0.2)), Vector((0.6, 0.06, 0.62)), Vector((0.935, 0.1, 0.5)),
                  Vector((0.935, 0.1, -0.4)), Vector((0.97, 0.02, BELT_TOP + 0.2))], 0.016, clips=(2, 3))
    finish(B, "Frame", coll)
    Cn = Builder()
    cone = lambda z, r: disc_ring(Vector((0, 0, z)), r, 28)
    Cn.tube_rings([cone(0.0, 0.3), cone(-1.15, 0.85)], "field_heat" if kind == "heat" else "field_cold",
                  (1.0, 0.45, 0.1) if kind == "heat" else (0.6, 0.85, 1.0), 0.02, 0.0, cap=False, smooth=True)
    cn = node(Cn, "Cone", coll, head + Vector((0, 0, -0.1)))
    cycle(cn, "scale", [Vector((1, 1, 1)), Vector((1.08, 1.08, 0.97))], 60)
    if kind == "heat":
        Co = Builder()
        for k in range(3):
            ring(Co, Vector((0, 0, -0.02 * k)), (0, 0, 1), 0.08 + k * 0.08, 0.1 + k * 0.08, 0.02, 24, "molten", C_MOLTEN, 0.05)
        co = node(Co, "Coil", coll, head + Vector((0, 0, 0.0)))
        cycle(co, "scale", [Vector((1, 1, 1)), Vector((1.12, 1.12, 1))], 30)
    else:
        Fn = Builder()
        for k in range(5):
            a = k / 5 * math.tau
            Fn.box(Vector((math.cos(a) * 0.1, math.sin(a) * 0.1, 0)), (0.16, 0.05, 0.01), Matrix.Rotation(a, 3, 'Z') @ Matrix.Rotation(0.4, 3, 'X'), "metal", (0.2, 0.2, 0.21), 0.1)
        Fn.cyl(Vector((0, 0, -0.01)), Vector((0, 0, 0.01)), 0.04, 10, "metal", C_DARK)
        fn = node(Fn, "Fan", coll, head + Vector((0, 0.0, 0.215)))
        spin(fn, 2, 4, 60)
    return coll

def build_heat_lamp():
    return emitter_cell("Concept_HeatLamp", "heat")

def build_cryo_vent():
    return emitter_cell("Concept_CryoVent", "cold")

# ======================================================================================
def build_counterweight_elevator():
    random.seed(961)
    coll = clear_collection("Concept_CounterweightElevator")
    B = Builder()
    top = 4.9
    posts = ((-0.92, -0.92), (0.92, -0.92), (0.92, 0.92), (-0.92, 0.92))
    for (x, y) in posts:
        B.box(Vector((x, y, (top - 1) / 2)), (0.1, 0.1, top + 1), I3, "metal", C_FRAME, 0.2, rust=0.3)
        for z in (1.0, 3.0):                                                 # splice plates where the post sections join
            B.box(Vector((x, y, z)), (0.12, 0.12, 0.2), I3, "steel", C_STEEL, 0.2, rust=0.5)
            stud(B, Vector((x, y + (0.06 if y > 0 else -0.06), z + 0.05)), (0, 1 if y > 0 else -1, 0), 0.011, 0.008)
            stud(B, Vector((x, y + (0.06 if y > 0 else -0.06), z - 0.05)), (0, 1 if y > 0 else -1, 0), 0.011, 0.008)
    for z in (1.0, 3.0):
        for sx in (-1, 1):
            B.cyl(Vector((sx * 0.92, -0.92, z - 0.9)), Vector((sx * 0.92, 0.92, z + 0.9)), 0.022, 6, "steel", C_STEEL, rust=0.5)
    B.box(Vector((0, 0, top)), (1.98, 1.98, 0.14), I3, "metal", C_DARK, 0.15)
    for sx in (-1, 1):
        B.box(Vector((sx * 0.45, 0, top - 0.3)), (0.08, 0.3, 0.5), I3, "metal", C_DARK, 0.15)                 # pulley cheeks
        B.box(Vector((sx * 0.53, 0, top - 0.3)), (0.08, 0.16, 0.16), I3, "steel", C_STEEL, 0.2, rust=0.4)      # bearing blocks
        for sy in (-1, 1):
            stud(B, Vector((sx * 0.57, sy * 0.055, top - 0.3)), (sx, 0, 0), 0.012, 0.01)
        gusset(B, Vector((sx * 0.45, 0.15, top - 0.07)), (0, 1, 0), (0, 0, -1), 0.18)
        gusset(B, Vector((sx * 0.45, -0.15, top - 0.07)), (0, -1, 0), (0, 0, -1), 0.18)
    motor(B, Vector((0.74, 0, top - 0.3)), (1, 0, 0), 0.12, 0.26)                                          # hoist brake motor on the shaft
    B.box(Vector((0.74, 0, top - 0.12)), (0.2, 0.08, 0.14), I3, "steel", C_STEEL, 0.2, rust=0.4)
    for k, (x, y) in enumerate(posts):
        a, b = Vector((x, y, top + 0.07)), Vector((posts[(k + 1) % 4][0], posts[(k + 1) % 4][1], top + 0.07))
        bolt_row(B, a.lerp(b, 0.1), a.lerp(b, 0.9), 6, (0, 0, 1), 0.012, 0.01)
    for x in (-0.45, 0.45):                                                  # cables the cages ride on
        for y in (-0.08, 0.08):
            B.cyl(Vector((x + (0.45 if x < 0 else -0.45) * 0.0, y, -0.95)), Vector((x, y, top - 0.35)), 0.012, 5, "steel", (0.2, 0.2, 0.2))
    slab(B, Vector((0, 0, -0.97)), (1.98, 1.98, 0.06), I3, "steel", C_STEEL, 0.25, rust=0.6)
    for x in (-0.45, 0.45):                                                  # spring buffers under each cage
        B.cyl(Vector((x, 0, -0.94)), Vector((x, 0, -0.93)), 0.16, 12, "metal", C_DARK, 0.15)
        B.cyl(Vector((x, 0.5, -0.93)), Vector((x, 0.5, -0.84)), 0.07, 10, "rubber", C_BLACK, 0.1)
        B.cyl(Vector((x, -0.5, -0.93)), Vector((x, -0.5, -0.84)), 0.07, 10, "rubber", C_BLACK, 0.1)
    for (x, y) in posts:
        stud(B, Vector((x * 0.9, y * 0.9, -0.94)), (0, 0, 1), 0.018, 0.014)
    B.box(Vector((0.92, -0.982, -0.2)), (0.18, 0.025, 0.26), I3, "panel", C_WHITE, 0.3)                     # call box
    B.box(Vector((0.95, -0.995, -0.16)), (0.04, 0.004, 0.04), I3, "panel", (0.8, 0.1, 0.06), 0.05)
    B.box(Vector((0.89, -0.995, -0.16)), (0.04, 0.004, 0.04), I3, "cyan", C_CYAN, 0.05)
    warn_plate(B, Vector((0.92, -0.9945, -0.27)), (0, -1, 0), 0.09)
    cable(B, [Vector((0.98, -0.95, -0.07)), Vector((0.985, -0.93, 1.0)), Vector((0.985, -0.93, 3.0)), Vector((0.95, -0.9, top - 0.08))],
          0.012, clips=(1, 2))
    hazard(B, Vector((-0.9, -0.99, -0.95)), (1, 0, 0), (0, 0, 1), 1.8, 0.08, (0, -1, 0), pitch=0.1)
    finish(B, "Frame", coll)
    Pu = Builder()
    ring(Pu, Vector((0, 0, 0)), (1, 0, 0), 0.3, 0.46, 0.14, 32, "steel", (0.3, 0.3, 0.31), 0.2, rust=0.4)
    for k in range(6):
        a = k / 6 * math.tau
        Pu.box(Vector((0, math.cos(a), math.sin(a))) * 0.18, (0.05, 0.3, 0.04), Matrix.Rotation(a, 3, 'X'), "steel", (0.3, 0.3, 0.31), 0.2)
    Pu.cyl(Vector((-0.9, 0, 0)), Vector((0.9, 0, 0)), 0.05, 10, "steel", C_STEEL)
    Pu.box(Vector((0.08, 0, 0.44)), (0.02, 0.08, 0.04), I3, "panel", C_YELLOW, 0.2)
    pul = node(Pu, "Pulley", coll, Vector((0, 0, top - 0.3)))
    hi_z, lo_z = 3.1, -0.9
    for name, sx, start in (("CageA", -1, hi_z), ("CageB", 1, lo_z)):
        Cg = Builder()
        Cg.box(Vector((0, 0, 0)), (0.84, 1.7, 0.06), I3, "steel", C_STEEL, 0.2, rust=0.4)
        Cg.box(Vector((0, 0, 0.9)), (0.84, 1.7, 0.05), I3, "metal", C_DARK, 0.2)
        for y in (-0.82, 0.82):
            for x in (-0.4, 0.4):
                Cg.box(Vector((x, y, 0.45)), (0.04, 0.04, 0.9), I3, "metal", C_DARK, 0.2)
        Cg.box(Vector((-sx * 0.4, 0, 0.45)), (0.02, 1.6, 0.8), I3, "panel", C_WHITE, 0.3)
        for y in (-0.4, 0.4):                                                # side panel stiffeners and rivets
            Cg.box(Vector((-sx * 0.414, y, 0.45)), (0.012, 0.04, 0.78), I3, "metal", C_DARK, 0.2)
        bolt_row(Cg, Vector((-sx * 0.412, -0.75, 0.84)), Vector((-sx * 0.412, 0.75, 0.84)), 6, (-sx, 0, 0), 0.01, 0.006)
        Cg.box(Vector((sx * 0.4, 0, 0.5)), (0.03, 1.64, 0.04), I3, "panel", C_YELLOW, 0.2)                # open side: a guard rail
        Cg.box(Vector((0, 0, 0.96)), (0.12, 0.3, 0.08), I3, "steel", C_STEEL, 0.2, rust=0.4)              # hanger and cable shackles
        for y in (-0.08, 0.08):
            ring(Cg, Vector((0, y, 1.03)), (1, 0, 0), 0.025, 0.04, 0.02, 8, "steel", C_STEEL)
        for y in (-0.6, -0.3, 0.0, 0.3, 0.6):                                # anti-slip strips on the floor
            decal(Cg, Vector((0, y, 0.031)), (0, 0, 1), 0.78, 0.03, "steel", L["C_WEAR"], 0.15, up=(0, 1, 0))
        hazard(Cg, Vector((-0.42, -0.851, 0.02)), (1, 0, 0), (0, 0, 1), 0.84, 0.06, (0, -1, 0), pitch=0.08)
        if name == "CageA":                                                  # its load: a couple of crates
            Cg.box(Vector((0, -0.3, 0.2)), (0.5, 0.5, 0.35), I3, "panel", C_WHITE, 0.3)
            Cg.box(Vector((0.05, 0.35, 0.18)), (0.45, 0.45, 0.3), I3, "metal", C_DARK, 0.2, rust=0.4)
        cg = node(Cg, name, coll, Vector((sx * 0.45, 0, start)))
        other = lo_z if start == hi_z else hi_z
        keys(cg, (1, 30, 75, 105, 150, 151), "location",
             [Vector((sx * 0.45, 0, start)), Vector((sx * 0.45, 0, start)), Vector((sx * 0.45, 0, other)),
              Vector((sx * 0.45, 0, other)), Vector((sx * 0.45, 0, start)), Vector((sx * 0.45, 0, start))])
    turns = (hi_z - lo_z) / (math.tau * 0.46)
    keys(pul, (1, 30, 75, 105, 150, 151), "rotation_euler",
         [(0, 0, 0), (0, 0, 0), (turns * math.tau, 0, 0), (turns * math.tau, 0, 0), (0, 0, 0), (0, 0, 0)])
    return coll

# ======================================================================================
def build_rail_gun():
    random.seed(971)
    coll = clear_collection("Concept_RailGun")
    B = Builder()
    y0, y1 = -0.95, 6.95
    slab(B, Vector((0, (y0 + y1) / 2, -0.95)), (1.9, y1 - y0, 0.1), I3, "steel", C_STEEL, 0.25, rust=0.6, bevel=0.02)
    for k in range(1, 4):                                                    # base plate section joints
        seam(B, Vector((-0.94, y0 + k * 2, -0.899)), Vector((0.94, y0 + k * 2, -0.899)), (0, 0, 1), 0.012)
    for k in range(8):
        y = y0 + 0.5 + k * (y1 - y0 - 1) / 7
        B.box(Vector((0, y, -0.65)), (1.2, 0.2, 0.5), I3, "metal", C_FRAME, 0.2, rust=0.3)                # rail supports
        for sx in (-1, 1):
            B.box(Vector((sx * 0.64, y, -0.88)), (0.08, 0.3, 0.04), I3, "steel", C_STEEL, 0.2, rust=0.5)
            stud(B, Vector((sx * 0.64, y - sx * 0.1, -0.86)), (0, 0, 1), 0.014, 0.012)
        streak(B, Vector((0.3, y - 0.101, -0.45)), (0, -1, 0), 0.03, 0.3)
    for sx in (-1, 1):
        B.box(Vector((sx * 0.38, (y0 + y1) / 2, -0.3)), (0.14, y1 - y0 - 0.2, 0.2), I3, "steel", (0.3, 0.3, 0.31), 0.2, rust=0.3)
        B.box(Vector((sx * 0.3, (y0 + y1) / 2, -0.3)), (0.02, y1 - y0 - 0.3, 0.16), I3, "copper", C_COPPER, 0.15)
        bolt_row(B, Vector((sx * 0.45, y0 + 0.3, -0.2)), Vector((sx * 0.45, y1 - 0.3, -0.2)), 12, (0, 0, 1), 0.011, 0.008)
        for k in range(6):                                                   # capacitor banks along the side
            c = Vector((sx * 0.8, 0.2 + k * 1.1, -0.55))
            B.cyl(c, c + Vector((0, 0, 0.5)), 0.12, 14, "panel", C_FACILITY, 0.1)
            ring(B, c + Vector((0, 0, 0.35)), (0, 0, 1), 0.12, 0.125, 0.03, 14, "cyan", C_CYAN, 0.05)
            for dx in (-0.045, 0.045):                                       # terminals, one bussed to the coil
                stud(B, c + Vector((dx, 0, 0.5)), (0, 0, 1), 0.018, 0.04, mat="copper", c=C_COPPER, rust=0.0)
            cable(B, [c + Vector((-sx * 0.045, 0, 0.54)), c + Vector((-sx * 0.12, 0, 0.6)), Vector((sx * 0.62, c.y + 0.4, -0.05))], 0.014)
        cable(B, [Vector((sx * 0.94, y0 + 0.4, -0.88)), Vector((sx * 0.94, 3.0, -0.88)), Vector((sx * 0.94, y1 - 0.5, -0.88))], 0.02, clips=(1,))
    for k in range(6):                                                       # accelerator coils
        y = 0.6 + k * 1.1
        ring(B, Vector((0, y, -0.25)), (0, 1, 0), 0.55, 0.68, 0.22, 32, "copper", C_COPPER, 0.15)
        ring(B, Vector((0, y, -0.25)), (0, 1, 0), 0.68, 0.72, 0.26, 32, "metal", C_DARK, 0.15)
        bolt_circle(B, Vector((0, y - 0.13, -0.25)), (0, -1, 0), 0.7, 6, 0.014, 0.01, phase=0.52)
    B.box(Vector((0, -0.6, -0.4)), (1.2, 0.7, 0.5), I3, "metal", C_BLUE, 0.2)                              # breech / loader
    B.box(Vector((0, -0.6, -0.12)), (0.9, 0.6, 0.04), I3, "steel", L["C_WEAR"], 0.12)
    for sx in (-1, 1):
        grille(B, Vector((sx * 0.601, -0.6, -0.42)), (sx, 0, 0), 0.5, 0.26, 5)
        bolt_row(B, Vector((sx * 0.55, -0.9, -0.14)), Vector((sx * 0.55, -0.3, -0.14)), 4, (0, 0, 1), 0.012, 0.01)
    warn_plate(B, Vector((0.35, -0.951, -0.35)), (0, -1, 0), 0.16)
    hazard(B, Vector((-0.6, -0.951, -0.6)), (1, 0, 0), (0, 0, 1), 1.2, 0.08, (0, -1, 0), pitch=0.1)
    ring(B, Vector((0, y1 - 0.1, -0.25)), (0, 1, 0), 0.5, 0.7, 0.18, 32, "metal", C_DARK, 0.15)
    ring(B, Vector((0, y1 - 0.02, -0.25)), (0, 1, 0), 0.5, 0.54, 0.03, 32, "cyan", C_CYAN, 0.05)
    for k in range(8):                                                       # muzzle cooling fins
        a = (k + 0.5) / 8 * math.tau
        B.box(Vector((math.cos(a) * 0.7, y1 - 0.25, -0.25 + math.sin(a) * 0.7)), (0.1, 0.3, 0.02),
              Matrix.Rotation(-a, 3, 'Y'), "metal", C_DARK, 0.15)
    finish(B, "Frame", coll)
    Sl = Builder()
    Sl.box(Vector((0, 0, 0)), (0.62, 0.5, 0.16), I3, "copper", C_COPPER, 0.15)
    Sl.box(Vector((0, 0, 0.1)), (0.5, 0.42, 0.04), I3, "metal", C_DARK, 0.1)
    Sl.box(Vector((0, 0.26, 0.1)), (0.5, 0.02, 0.16), I3, "cyan", C_CYAN, 0.05)
    sled = node(Sl, "Sled", coll, Vector((0, -0.3, -0.22)))
    keys(sled, (1, 60, 66, 70, 120, 121), "location",
         [Vector((0, -0.3, -0.22)), Vector((0, -0.3, -0.22)), Vector((0, 6.4, -0.22)), Vector((0, 6.4, -0.22)),
          Vector((0, -0.3, -0.22)), Vector((0, -0.3, -0.22))])
    Ch = Builder()
    for k in range(6):
        Ch.box(Vector((0, k * 0.12, 0.2)), (0.02, 0.08, 0.4), I3, "cyan", C_CYAN, 0.05)
    chg = node(Ch, "Charge", coll, Vector((0.61, -0.95, -0.55)))
    keys(chg, (1, 58, 64, 121), "scale", [Vector((1, 1, 0.05)), Vector((1, 1, 1)), Vector((1, 1, 0.05)), Vector((1, 1, 0.05))])
    return coll

# ======================================================================================
def build_tipping_bucket():
    random.seed(981)
    coll = clear_collection("Concept_TippingBucket")
    B = Builder()
    piv = Vector((0, 0, 0.9))
    for sy in (-1, 1):
        for sx in (-1, 1):
            B.cyl(Vector((sx * 0.8, sy * 0.85, -0.95)), Vector((0, sy * 0.85, piv.z + 0.1)), 0.04, 8, "steel", C_STEEL, rust=0.5)
        B.cyl(Vector((0, sy * 0.85, piv.z - 0.08)), Vector((0, sy * 0.85, piv.z + 0.08)), 0.1, 12, "metal", C_DARK, rust=0.3)
        B.cyl(Vector((0, sy * 0.87, piv.z)), Vector((0, sy * 0.93, piv.z)), 0.07, 12, "metal", C_BLUE, 0.2)   # bearing cap
        bolt_circle(B, Vector((0, sy * 0.93, piv.z)), (0, sy, 0), 0.045, 4, 0.01, 0.008, phase=0.785)
        B.cyl(Vector((-0.55, sy * 0.85, -0.3)), Vector((0.55, sy * 0.85, -0.3)), 0.025, 6, "steel", C_STEEL, rust=0.5)  # leg tie
        for sx in (-1, 1):
            B.box(Vector((sx * 0.8, sy * 0.85, -0.93)), (0.2, 0.14, 0.02), I3, "steel", C_STEEL, 0.2, rust=0.5)
            stud(B, Vector((sx * 0.86, sy * 0.85, -0.92)), (0, 0, 1), 0.014, 0.012)
    slab(B, Vector((0, 0, -0.97)), (1.98, 1.98, 0.06), I3, "steel", C_STEEL, 0.25, rust=0.6)
    for sx in (-1, 1):                                                       # exit slides, one each side
        B.box(Vector((sx * 0.6, 0, -0.55)), (0.8, 1.5, 0.04), Matrix.Rotation(sx * math.radians(18), 3, 'Y'), "steel", L["C_WEAR"], 0.15)
        B.box(Vector((sx * 0.6, 0.75, -0.45)), (0.8, 0.04, 0.3), I3, "panel", C_WHITE, 0.3)
        B.box(Vector((sx * 0.6, -0.75, -0.45)), (0.8, 0.04, 0.3), I3, "panel", C_WHITE, 0.3)
    fz = 2.3                                                                 # intake funnel on top
    B.tube_rings([quad_ring(0, 0, fz + 0.65, 0.9, 0.9), quad_ring(0, 0, fz, 0.3, 0.3)], "steel", (0.3, 0.3, 0.3), 0.2, 0.5, cap=False)
    B.tube_rings([quad_ring(0, 0, fz - 0.02, 0.3, 0.3), quad_ring(0, 0, fz + 0.63, 0.92, 0.92)], "steel", (0.3, 0.3, 0.3), 0.2, 0.5, cap=False)
    for (x, y) in ((-0.9, -0.9), (0.9, -0.9), (0.9, 0.9), (-0.9, 0.9)):
        B.box(Vector((x, y, (fz + 0.65 + piv.z) / 2)), (0.08, 0.08, fz + 0.65 - piv.z), I3, "metal", C_FRAME, 0.2, rust=0.3)
    for k in range(4):                                                       # funnel rim angle and seam welds
        a = k / 4 * math.tau
        d, t = Vector((math.cos(a), math.sin(a), 0)), Vector((-math.sin(a), math.cos(a), 0))
        B.box(d * 0.9 + Vector((0, 0, fz + 0.62)), (0.06, 1.8, 0.06), Matrix.Rotation(a, 3, 'Z'), "metal", C_DARK, 0.15)
        bolt_row(B, d * 0.9 + t * -0.7 + Vector((0, 0, fz + 0.65)), d * 0.9 + t * 0.7 + Vector((0, 0, fz + 0.65)), 5, (0, 0, 1), 0.011, 0.008)
        corner = (d + t) * 0.9
        B.pipe([corner * (0.3 / 0.9) + Vector((0, 0, fz + 0.01)), corner + Vector((0, 0, fz + 0.64))], 0.01, 4, "steel", (0.25, 0.25, 0.25))
    hazard(B, Vector((-0.9, -0.941, fz + 0.5)), (1, 0, 0), (0, 0, 1), 1.8, 0.12, (0, -1, 0), pitch=0.1)
    warn_plate(B, Vector((0.0, -0.625, fz + 0.35)), Vector((0, -0.65, -0.6)).normalized(), 0.16)
    finish(B, "Frame", coll)
    Bk = Builder()
    Bk.cyl(Vector((0, -0.85, 0)), Vector((0, 0.85, 0)), 0.05, 10, "steel", C_STEEL)
    for sx in (-1, 1):                                                       # two scoops back to back
        Bk.box(Vector((sx * 0.4, 0, -0.18)), (0.8, 1.4, 0.04), I3, "steel", (0.3, 0.3, 0.3), 0.2, rust=0.5)
        Bk.box(Vector((sx * 0.79, 0, -0.02)), (0.04, 1.4, 0.36), I3, "panel", C_WHITE, 0.3)
        for sy in (-1, 1):
            Bk.box(Vector((sx * 0.4, sy * 0.7, -0.02)), (0.8, 0.04, 0.36), I3, "panel", C_WHITE, 0.3)
        hazard(Bk, Vector((sx * 0.811, -0.6 * sx, -0.12)), (0, sx, 0), (0, 0, 1), 1.2, 0.06, (sx, 0, 0), pitch=0.08)
    for sx in (-1, 1):                                                       # stiffening ribs under each scoop
        for y in (-0.4, 0.4):
            Bk.box(Vector((sx * 0.4, y, -0.22)), (0.74, 0.03, 0.05), I3, "metal", C_DARK, 0.2)
        for sy in (-1, 1):
            bolt_row(Bk, Vector((sx * 0.15, sy * 0.721, 0.1)), Vector((sx * 0.7, sy * 0.721, 0.1)), 4, (0, sy, 0), 0.01, 0.007)
    Bk.box(Vector((0, 0, 0.05)), (0.06, 1.4, 0.5), I3, "metal", C_DARK, 0.2)                               # centre divider
    bk = node(Bk, "Bucket", coll, piv)
    t = math.radians(28)
    keys(bk, (1, 45, 52, 105, 112, 121), "rotation_euler",
         [(0, t, 0), (0, t, 0), (0, -t, 0), (0, -t, 0), (0, t, 0), (0, t, 0)])
    return coll

# ======================================================================================
def build_assembly_chamber():
    random.seed(991)
    coll = clear_collection("Concept_AssemblyChamber")
    B, G = Builder(), Builder()
    lo, hi = Vector((-2.95, -2.95, -1.0)), Vector((2.95, 2.95, 4.95))
    ctr = (lo + hi) / 2
    for x in (lo.x, hi.x):                                                   # the twelve edges
        for y in (lo.y, hi.y):
            B.box(Vector((x * 0.99, y * 0.99, ctr.z)), (0.16, 0.16, hi.z - lo.z), I3, "metal", C_DARK, 0.15)
    for z in (lo.z + 0.08, hi.z - 0.08, 1.0):
        for y in (lo.y, hi.y):
            B.box(Vector((0, y * 0.99, z)), (5.9, 0.16, 0.16), I3, "metal", C_DARK, 0.15)
        for x in (lo.x, hi.x):
            B.box(Vector((x * 0.99, 0, z)), (0.16, 5.9, 0.16), I3, "metal", C_DARK, 0.15)
    for n in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, -1, 0))):
        base = n * 2.93
        panel_face(B, base + Vector((0, 0, 0.0)), n, 5.6, 1.9, C_FACILITY)                                   # solid lower band
        R = facing_basis(n)
        for k in range(3):
            G.box(base + R @ Vector((-1.9 + k * 1.9, 2.95 - 0.0, 0)) + Vector((0, 0, 0)), (1.8, 3.7, 0.02), R, "glass", (0.55, 0.9, 1.0), 0.02)
            B.box(base + R @ Vector((-2.85 + k * 1.9, 2.95, 0.02)), (0.08, 3.8, 0.04), R, "panel", C_WHITE, 0.3)
    B.box(Vector((0, 0, hi.z - 0.05)), (5.9, 5.9, 0.1), I3, "panel", C_FACILITY, 0.1)                         # roof
    for k in range(4):
        B.box(Vector((-1.5 + k, 0, hi.z + 0.02)), (0.6, 4.0, 0.06), I3, "metal", C_DARK, 0.1)
    B.box(Vector((0, 0, lo.z + 0.03)), (5.9, 5.9, 0.06), I3, "metal", (0.06, 0.06, 0.07), 0.1)
    ring(B, Vector((0, 0, lo.z + 0.07)), (0, 0, 1), 1.6, 1.7, 0.02, 48, "cyan", C_CYAN, 0.05)
    for s in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, -1, 0))):                  # intake ports
        p = s * 2.95 + Vector((0, 0, 0.3))
        ring(B, p, s, 0.55, 0.72, 0.3, 32, "metal", C_DARK, 0.15)
        ring(B, p - s * 0.1, s, 0.55, 0.58, 0.04, 32, "cyan", C_CYAN, 0.05)
        bolt_circle(B, p + s * 0.15, s, 0.64, 12, 0.016, 0.012, phase=0.13)
        R = facing_basis(s)
        for k in (-1, 1):                                                    # lower band: vents, seams, run-off
            grille(B, s * 2.95 + R @ Vector((k * 1.7, -0.2, 0)), s, 0.9, 0.3, 5)
            seam(B, s * 2.95 + R @ Vector((k * 0.95, -0.9, 0)), s * 2.95 + R @ Vector((k * 0.95, 0.75, 0)), s)
            streak(B, s * 2.95 + R @ Vector((k * 0.85, 0.2, 0)), s, 0.04, 0.5, mat="panel")
        bolt_row(B, s * 3.0 + R @ Vector((-2.6, 1.0, 0)), s * 3.0 + R @ Vector((2.6, 1.0, 0)), 14, s, 0.013, 0.01)
        warn_plate(B, s * 2.95 + R @ Vector((1.3, 0.45, 0)), s, 0.2)
    for x in (lo.x, hi.x):                                                   # corner gussets at the mid rail
        for y in (lo.y, hi.y):
            for d in (Vector((-math.copysign(1, x), 0, 0)), Vector((0, -math.copysign(1, y), 0))):
                c = Vector((x * 0.99, y * 0.99, 1.0))
                gusset(B, c + d * 0.08 + Vector((0, 0, -0.08)), d, (0, 0, -1), 0.3, col=C_DARK)
    for k in range(2):                                                       # roof conduits feeding the top emitters
        y = -2.6 + k * 5.2
        B.pipe([Vector((-2.6, y, hi.z + 0.025)), Vector((2.6, y, hi.z + 0.025))], 0.025, 8, "steel", C_STEEL)
        for x in (-1.8, -0.6, 0.6, 1.8):
            B.box(Vector((x, y, hi.z + 0.025)), (0.05, 0.1, 0.05), I3, "steel", C_STEEL, 0.2, rust=0.3)
    hatch = Vector((0, 2.96, -0.55))
    B.box(hatch, (1.6, 0.08, 0.8), I3, "metal", (0.02, 0.02, 0.02), 0.05)
    for sx in (-1, 1):
        B.box(Vector((sx * 0.82, 2.98, -0.55)), (0.08, 0.06, 0.9), I3, "panel", C_YELLOW, 0.2)
    B.box(Vector((0, 2.98, -0.08)), (1.72, 0.06, 0.08), I3, "panel", C_YELLOW, 0.2)
    B.box(Vector((1.3, 2.975, -0.65)), (0.36, 0.05, 0.44), I3, "panel", C_WHITE, 0.3)                       # hatch control panel
    B.box(Vector((1.3, 3.001, -0.57)), (0.26, 0.004, 0.14), I3, "cyan", C_CYAN, 0.05)
    for k in range(3):
        B.box(Vector((1.22 + k * 0.08, 3.001, -0.76)), (0.05, 0.006, 0.05), I3, "panel", (C_YELLOW, (0.8, 0.1, 0.06), C_YELLOW)[k], 0.05)
    for x in (-2.6, 2.6):                                                    # zero-point emitter pods in the corners
        for y in (-2.6, 2.6):
            for z in (-0.6, 4.55):
                p = Vector((x, y, z))
                B.box(p, (0.35, 0.35, 0.3), I3, "panel", C_WHITE, 0.3)
                d = (ctr - p).normalized()
                B.cyl(p + d * 0.18, p + d * 0.26, 0.1, 12, "cyan", C_CYAN, 0.05)
                ring(B, p + d * 0.2, d, 0.1, 0.125, 0.05, 12, "metal", C_DARK, 0.15)
                grille(B, p + Vector((0, 0, 0.151 if z < 0 else -0.151)), (0, 0, 1 if z < 0 else -1), 0.24, 0.24, 3)
    finish(B, "Frame", coll); finish(G, "Glass", coll)
    F = Builder()
    F.box(Vector((0, 0, 0)), (5.5, 5.5, 5.5), I3, "field_zp", C_CYAN, 0.02)
    fld = node(F, "Field", coll, ctr)
    cycle(fld, "scale", [Vector((1, 1, 1)), Vector((0.97, 0.97, 0.97))], 120)
    Pa = Builder()                                                           # a frame mid-assembly: plates and rods drifting
    for k in range(4):
        a = k / 4 * math.tau
        Pa.box(Vector((math.cos(a) * 0.9, math.sin(a) * 0.9, 0.2 * (k % 2))), (0.9, 0.9, 0.05), Matrix.Rotation(a + 0.3, 3, 'Z') @ Matrix.Rotation(0.3 * (k - 1.5), 3, 'X'),
               "steel", (0.55, 0.55, 0.58), 0.15)
    for k in range(3):
        a = k / 3 * math.tau + 0.5
        Pa.cyl(Vector((math.cos(a) * 0.4, math.sin(a) * 0.4, -0.6)), Vector((math.cos(a) * 0.6, math.sin(a) * 0.6, 0.7)), 0.05, 8, "copper", C_COPPER, 0.15)
    Pa.box(Vector((0, 0, 0.1)), (0.8, 0.8, 0.8), I3, "metal", C_DARK, 0.2)
    for k in range(4):
        a = k / 4 * math.tau
        Pa.box(Vector((math.cos(a) * 0.42, math.sin(a) * 0.42, 0.1)), (0.06, 0.06, 0.82), I3, "cyan", C_CYAN, 0.05)
    parts = node(Pa, "Parts", coll, ctr + Vector((0, 0, -0.3)))
    keys(parts, (1, 121, 241), "rotation_euler", [(0, 0, 0), (0.15, 0.1, math.pi / 2), (0, 0, math.pi)], linear=True)
    keys(parts, (1, 61, 121, 181, 241), "location", [ctr + Vector((0, 0, -0.3)), ctr + Vector((0, 0, 0.1)), ctr + Vector((0, 0, -0.3)),
                                                      ctr + Vector((0, 0, 0.1)), ctr + Vector((0, 0, -0.3))])
    return coll

# ======================================================================================
def build_screw_elevator():
    random.seed(1001)
    coll = clear_collection("Concept_ScrewElevator")
    B, G = Builder(), Builder()
    z0, z1, r = -0.55, 4.7, 0.72
    glass_tube(G, Vector((0, 0, z0)), Vector((0, 0, z1)), r)
    for z in (z0 + 0.05, 1.0, 3.0, z1 - 0.05):
        ring(B, Vector((0, 0, z)), (0, 0, 1), r, r + 0.12, 0.12, 32, "metal", C_DARK, 0.15, rust=0.2)
        for s in (-1, 1):
            if z + s * 0.06 > -0.55:                                         # clamp bolts on the band faces
                bolt_circle(B, Vector((0, 0, z + s * 0.06)), (0, 0, s), r + 0.07, 8, 0.013, 0.01, phase=0.2)
        for (x, y) in ((-1, -1), (1, -1), (1, 1), (-1, 1)):                  # stays out to the posts
            d = Vector((x, y, 0)).normalized()
            B.box(d * (r + 0.27) + Vector((0, 0, z)), (0.34, 0.03, 0.06), Matrix.Rotation(math.atan2(y, x), 3, 'Z'), "metal", C_FRAME, 0.2, rust=0.3)
    for (x, y) in ((-0.8, -0.8), (0.8, -0.8), (0.8, 0.8), (-0.8, 0.8)):
        B.box(Vector((x, y, (z1 - 1) / 2 + 0.1)), (0.08, 0.08, z1 + 1.1), I3, "metal", C_FRAME, 0.2, rust=0.3)
    slab(B, Vector((0, 0, -0.78)), (1.9, 1.9, 0.44), I3, "metal", C_FRAME, 0.2, rust=0.3, bevel=0.03)       # drive base
    panel_face(B, Vector((0, 0.955, -0.78)), (0, 1, 0), 1.7, 0.36, C_WHITE)
    for sx in (-1, 1):
        panel_face(B, Vector((sx * 0.955, 0, -0.78)), (sx, 0, 0), 1.7, 0.36, C_WHITE)
        grille(B, Vector((sx * 0.973, 0.3, -0.8)), (sx, 0, 0), 0.6, 0.18, 4)
        for k in range(4):
            stud(B, Vector((sx * 0.973, -0.8 + k * 0.53, -0.62)), (sx, 0, 0), 0.011, 0.007)
    motor(B, Vector((0.55, 0.6, -0.4)), (0, 0, 1), 0.2, 0.3)                                               # drive motor
    B.box(Vector((0.25, 0.3, -0.5)), (0.5, 0.14, 0.1), I3, "metal", C_BLUE, 0.2)                          # gear train cover
    cable(B, [Vector((0.55, 0.85, -0.3)), Vector((0.85, 0.88, -0.4)), Vector((0.8, 0.88, 0.5)), Vector((0.8, 0.88, 3.8)),
              Vector((0.4, 0.8, 4.2))], 0.014, clips=(2, 3))
    warn_plate(B, Vector((-0.5, 0.973, -0.75)), (0, 1, 0), 0.16)
    # intake at the back at belt height, spout at the top front
    B.box(Vector((0, -0.85, -0.45)), (0.9, 0.3, 0.5), I3, "metal", C_DARK, 0.15)
    B.box(Vector((0, -0.97, -0.5)), (0.7, 0.04, 0.34), I3, "metal", (0.02, 0.02, 0.02), 0.05)
    hazard(B, Vector((-0.45, -1.0, -0.2)), (1, 0, 0), (0, 0, 1), 0.9, 0.05, (0, -1, 0), pitch=0.08)
    B.box(Vector((0, 0.72, 4.2)), (0.8, 0.5, 0.5), I3, "metal", C_DARK, 0.15)
    B.box(Vector((0, 0.9, 3.95)), (0.7, 0.3, 0.04), Matrix.Rotation(math.radians(-20), 3, 'X'), "steel", L["C_WEAR"], 0.12)
    B.cyl(Vector((0, 0, z1)), Vector((0, 0, z1 + 0.25)), 0.5, 20, "metal", C_DARK, 0.15)                      # gearbox cap
    B.cyl(Vector((0, 0, z1 + 0.25)), Vector((0, 0, z1 + 0.28)), 0.3, 20, "glow", C_AMBER, 0.05)
    bolt_circle(B, Vector((0, 0, z1 + 0.25)), (0, 0, 1), 0.42, 10, 0.016, 0.012)
    for k in range(6):                                                       # gearbox ribs
        a_ = k / 6 * math.tau + 0.3
        B.box(Vector((math.cos(a_) * 0.5, math.sin(a_) * 0.5, z1 + 0.12)), (0.06, 0.03, 0.2), Matrix.Rotation(a_, 3, 'Z'), "metal", C_DARK, 0.15)
    finish(B, "Frame", coll); finish(G, "Glass", coll)
    Sc = Builder()
    Sc.cyl(Vector((0, 0, 0)), Vector((0, 0, z1 - z0)), 0.08, 12, "steel", C_STEEL)
    helix(Sc, 0.1, z1 - z0 - 0.1, 5.0, 0.08, r - 0.04, 0.03, 160, "steel", (0.45, 0.45, 0.47))
    helix(Sc, 0.1 + 0.03, z1 - z0 - 0.1 + 0.03, 5.0, r - 0.1, r - 0.04, 0.012, 160, "panel", C_YELLOW)
    for z in (0.02, z1 - z0 - 0.02):                                         # shaft collars
        ring(Sc, Vector((0, 0, z)), (0, 0, 1), 0.08, 0.13, 0.06, 12, "steel", C_STEEL)
    sc_ = node(Sc, "Screw", coll, Vector((0, 0, z0)))
    spin(sc_, 2, -1, 45)
    return coll

# ======================================================================================
PT_RUN = (0.0, 4.0)          # sprocket centres (z)
PT_R = 0.9                   # loop radius; runs at y = 1 -+ PT_R
def pt_loop(u):
    """Point on the chain loop (x = 0), u in [0, 1): up the back run, over the top, down the front run."""
    y0, zc0, zc1, r = 1.0, PT_RUN[0], PT_RUN[1], PT_R
    straight = zc1 - zc0
    arc = math.pi * r
    total = 2 * straight + 2 * arc
    s = (u % 1.0) * total
    if s < straight:
        return Vector((0, y0 - r, zc0 + s))
    s -= straight
    if s < arc:
        a = s / r
        return Vector((0, y0 - r * math.cos(a), zc1 + r * math.sin(a)))
    s -= arc
    if s < straight:
        return Vector((0, y0 + r, zc1 - s))
    s -= straight
    a = s / r
    return Vector((0, y0 + r * math.cos(a), zc0 - r * math.sin(a)))

def build_platform_elevator():
    random.seed(1011)
    coll = clear_collection("Concept_PlatformElevator")
    B = Builder()
    for sx in (-1, 1):
        x = sx * 0.92
        for y in (-0.9, 2.9):
            B.box(Vector((x, y, 2.0)), (0.1, 0.1, 6.0), I3, "metal", C_FRAME, 0.2, rust=0.3)
        for z in (-0.95, 1.5, 3.5, 4.95):
            B.box(Vector((x, 1.0, z)), (0.1, 3.9, 0.1), I3, "metal", C_FRAME, 0.2, rust=0.3)
        for (ya, yb, za, zb) in ((-0.9, 2.9, -0.9, 1.4), (-0.9, 2.9, 3.6, 1.6)):
            B.cyl(Vector((x, ya, za)), Vector((x, yb, zb)), 0.02, 6, "steel", C_STEEL, rust=0.5)
        chain = [pt_loop(i / 64) + Vector((x * 0.9, 0, 0)) for i in range(64)]
        B.pipe(chain + [chain[0]], 0.025, 5, "steel", (0.2, 0.2, 0.2))
        panel_face(B, Vector((sx * 0.975, 1.0, -0.5)), (sx, 0, 0), 3.6, 0.7, C_WHITE)
        for y in (-0.9, 2.9):
            foot(B, x, y, 0.1)
            for z in (1.5, 3.5, 4.95):                                       # joint gussets
                gusset(B, Vector((x, y + (0.05 if y < 0 else -0.05), z - 0.05)), (0, 1 if y < 0 else -1, 0), (0, 0, -1), 0.22)
        for y in (0.1, 1.0, 1.9):                                            # lower panel: bolts and vents
            stud(B, Vector((sx * 0.993, y, -0.2)), (sx, 0, 0), 0.012, 0.008)
            stud(B, Vector((sx * 0.993, y, -0.8)), (sx, 0, 0), 0.012, 0.008)
        grille(B, Vector((sx * 0.993, 1.45, -0.5)), (sx, 0, 0), 0.6, 0.3, 5)
        streak(B, Vector((sx * 0.993, 0.1, -0.21)), (sx, 0, 0), 0.03, 0.25)
        for z in (PT_RUN[0], PT_RUN[1]):                                     # bearing blocks for the sprocket shafts
            B.box(Vector((x, 1.0, z)), (0.14, 0.2, 0.2), I3, "metal", C_BLUE, 0.2, rust=0.15)
            for dy in (-0.07, 0.07):
                stud(B, Vector((x + sx * 0.07, 1.0 + dy, z + 0.06)), (sx, 0, 0), 0.012, 0.01)
                stud(B, Vector((x + sx * 0.07, 1.0 + dy, z - 0.06)), (sx, 0, 0), 0.012, 0.01)
            B.box(Vector((x, 1.0, z + (0.45 if z > 1 else -0.45))), (0.08, 0.08, 0.9), I3, "metal", C_FRAME, 0.2, rust=0.3)
    slab(B, Vector((0, 1.0, -0.97)), (1.98, 3.96, 0.06), I3, "steel", C_STEEL, 0.25, rust=0.6)
    motor(B, Vector((0.7, -0.5, -0.65)), (0, 1, 0), 0.18, 0.4)                                             # drive motor
    B.box(Vector((0.7, -0.5, -0.9)), (0.36, 0.44, 0.1), I3, "metal", C_FRAME, 0.2, rust=0.3)
    B.box(Vector((0.86, 0.3, -0.28)), (0.06, 1.32, 0.16), Matrix.Rotation(0.43, 3, 'X'), "metal", C_BLUE, 0.2)   # drive chain guard
    cable(B, [Vector((0.7, -0.3, -0.45)), Vector((0.5, -0.7, -0.93)), Vector((-0.8, -0.8, -0.93)), Vector((-0.86, -0.84, 0.6))],
          0.016, clips=(1, 2))
    B.box(Vector((-0.86, -0.97, 0.6)), (0.22, 0.04, 0.3), I3, "panel", C_WHITE, 0.3)                      # call box
    B.box(Vector((-0.9, -0.992, 0.65)), (0.04, 0.004, 0.04), I3, "panel", (0.8, 0.1, 0.06), 0.05)
    B.box(Vector((-0.82, -0.992, 0.65)), (0.04, 0.004, 0.04), I3, "panel", C_YELLOW, 0.05)
    warn_plate(B, Vector((-0.86, -0.9915, 0.53)), (0, -1, 0), 0.1)
    hazard(B, Vector((-0.9, -0.99, -0.94)), (1, 0, 0), (0, 0, 1), 1.8, 0.08, (0, -1, 0), pitch=0.1)
    hazard(B, Vector((0.9, 2.99, -0.94)), (-1, 0, 0), (0, 0, 1), 1.8, 0.08, (0, 1, 0), pitch=0.1)
    finish(B, "Frame", coll)
    for name, zc in (("SprocketBottom", PT_RUN[0]), ("SprocketTop", PT_RUN[1])):
        Sp = Builder()
        for sx in (-1, 1):
            ring(Sp, Vector((sx * 0.83, 0, 0)), (1, 0, 0), PT_R - 0.12, PT_R + 0.02, 0.05, 32, "steel", (0.3, 0.3, 0.31), 0.2, rust=0.4)
            for k in range(12):
                a = k / 12 * math.tau
                Sp.box(Vector((sx * 0.83, math.cos(a) * (PT_R + 0.04), math.sin(a) * (PT_R + 0.04))), (0.05, 0.06, 0.06),
                       Matrix.Rotation(a, 3, 'X'), "steel", (0.3, 0.3, 0.31), 0.2)
        Sp.cyl(Vector((-0.9, 0, 0)), Vector((0.9, 0, 0)), 0.06, 10, "steel", C_STEEL)
        sp = node(Sp, name, coll, Vector((0, 1.0, zc)))
        spin(sp, 0, 1.0 * (2 * (PT_RUN[1] - PT_RUN[0]) + 2 * math.pi * PT_R) / (math.tau * PT_R) / 2, 180)
    length = 180
    for k in range(6):
        Pl = Builder()
        Pl.box(Vector((0, 0, 0)), (1.5, 0.75, 0.05), I3, "steel", C_STEEL, 0.2, rust=0.4)
        for x in (-0.5, 0.0, 0.5):                                           # tread and bolts
            decal(Pl, Vector((x, 0, 0.025)), (0, 0, 1), 0.04, 0.66, "steel", L["C_WEAR"], 0.15, up=(0, 1, 0))
        for sx in (-1, 1):
            for sy in (-1, 1):
                stud(Pl, Vector((sx * 0.7, sy * 0.3, 0.025)), (0, 0, 1), 0.012, 0.008)
            Pl.cyl(Vector((sx * 0.81, 0, 0.0)), Vector((sx * 0.84, 0, 0.0)), 0.035, 8, "steel", C_STEEL)   # chain pins
        Pl.box(Vector((0, 0.36, 0.06)), (1.5, 0.03, 0.12), I3, "panel", C_YELLOW, 0.2)
        Pl.box(Vector((0, -0.36, 0.06)), (1.5, 0.03, 0.12), I3, "panel", C_YELLOW, 0.2)
        for sx in (-1, 1):
            Pl.box(Vector((sx * 0.78, 0, 0.12)), (0.06, 0.06, 0.3), I3, "metal", C_DARK, 0.2)
        ob = node(Pl, f"Platform{k}", coll, pt_loop(k / 6))
        n = 30
        frames = [1 + round(length * i / n) for i in range(n + 1)]
        keys(ob, frames, "location", [pt_loop(k / 6 + i / n) for i in range(n + 1)], linear=True)
    return coll

PIECES = [
    (build_gravity_inverter, "concept_gravity_inverter.glb"),
    (build_tag_gate, "concept_tag_gate.glb"),
    (build_bounce_pad, "concept_bounce_pad.glb"),
    (build_vortex, "concept_vortex_funnel.glb"),
    (build_tube_straight, "concept_tube_straight.glb"),
    (build_tube_bend, "concept_tube_bend.glb"),
    (build_tube_junction, "concept_tube_junction.glb"),
    (build_tube_receiver, "concept_tube_receiver.glb"),
    (build_heat_lamp, "concept_heat_lamp.glb"),
    (build_cryo_vent, "concept_cryo_vent.glb"),
    (build_counterweight_elevator, "concept_counterweight_elevator.glb"),
    (build_rail_gun, "concept_rail_gun.glb"),
    (build_tipping_bucket, "concept_tipping_bucket.glb"),
    (build_assembly_chamber, "concept_assembly_chamber.glb"),
    (build_screw_elevator, "concept_screw_elevator.glb"),
    (build_platform_elevator, "concept_platform_elevator.glb"),
]
ICONS = []

def build_all(do_export=True):
    return build_family(PIECES, OUT_DIR, do_export)

if __name__ == "__main__":
    build_all(do_export=False)
