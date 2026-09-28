# Physics cookbook: numbers that make behaviours happen

Box3D mixes friction between two surfaces (assume roughly the geometric mean; verify in-engine). Grid cells
are 2 m, the belt top is at cell floor + 0.15 (`BELT_TOP = -0.85` in cell-local coordinates), and item bodies
are about 0.3–0.9 m. These are the design values used so far. They are unverified in Godot, so treat them as
starting points.

## Thresholds

| Behaviour | Rule | Used where |
|---|---|---|
| slides on a slope | tan(angle) > μ; 25° gives 0.47, 19° gives 0.34, 8° gives 0.14 | shale (μ 0.35) slides at 25° and parks below ~19° |
| rolls on a slope | a sphere with low rolling resistance rolls on almost any slope | scree (rolling 0.03), ballast (0.02) |
| stays on a turntable | stays while r < μ·g/ω² | at 40°/s (ω 0.70, ω² 0.49): puck (mixed μ ~0.16) leaves beyond ~3 m; burr (~1.2) never leaves |
| falls through a sieve | item diameter < slot width | 0.5 m pebbles through 0.54 m slots; 0.8 m slabs don't |
| tips a hinged deck | sled torque vs item torque | deck density 0.3: one 3.6-mass ballast shot at 6 m outweighs a 2×0.8×2.6 sled at 6 m |
| won't be carried by a belt | item friction far below belt friction | frost (0.02), puck (0.03): the belt slips under them |
| bogs down | a floor patch with friction 3 | the Tar Pit floor |
| won't bounce | restitution 0, high rolling resistance | tar (0/3.0), latex (0/2.0) |
| bounces | restitution 0.95 (rubber ball), 1.25 (bounce pad surface) | rubber ball, bounce pad |
| drifts | density ≤ 0.15, gravity_scale 0.5–0.6, linear_damping 0.8–0.9 | floatstone, aerogel |

## Item property ranges

- density: 0.05 (aerogel) … 1–3 (rocks) … 7–8 (metals) … 13 (quicksilver) … 20 (ballast)
- friction: 0.02–0.05 (slippery) … 0.3–0.6 (normal) … 0.9 (grippy) … 1.6–2.0 (sticky)
- rolling_resistance: 0 (beads) … 0.02–0.1 (balls, rods) … 0.5–1.2 (lumps) … 2–3 (blobs)

## Collider shapes available on Box3DBody

`shape_type`: 0 Box (`box_size`), 1 Sphere (`sphere_radius`), 2 Capsule (`capsule_radius`, `capsule_height`,
total height along local Y), 3 Cylinder, 4 Cone, 5 Hull, 6 Mesh, 7 Fit Mesh, 8 Height Field. Cylinder sizing is
unverified, and Hull/Mesh need a direct MeshInstance child. Items use box, sphere or capsule only. Pucks use a
box so they slide flat.
