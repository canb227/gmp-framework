# Pipeline gotchas (each one cost real time)

## Godot files
- **UIDs use a base-35 alphabet**: `a-z` then `0-8`, with **no 9**. `scenegen.uid_text` does this now. A uid
  containing 9 gets reassigned by Godot, and every reference to it goes stale.
- **Keep ext_resource ids stable** when regenerating a scene someone has edited by hand. `place_museum.py` keeps
  existing `hall_` ids, because hand-placed nodes (e.g. `SpawnerConveyorLine`) reference them.
- **Godot adds `unique_id=` to nodes** on save. Regenerated nodes lose them, which is harmless.
- **Transform3D text is row-major** in .tscn: `Transform3D(xx, xy, xz, yx, ...)` gives rows, not columns. Use
  the `xf(cols, pos)` helper in `place_museum.py`.
- **Typed dictionaries** in scenes: `itemWeights = Dictionary[String, float]({"a": 1.0})`.
- **Label3D** text needs `\n` escaped as `\\n` in the file. Wrapping needs `autowrap_mode = 3` plus `width` in
  pixels (metres ÷ `pixel_size`). With yaw 0 a label faces +Z, and yaw 1 faces +X.

## Box3D
- The GDExtension isn't visible to C#. Scripts extend `Node3D` and use `Call("set_angular_velocity", ...)`,
  `Set(...)` and so on (see `GMPOBox3DBody`).
- `body_type`: 0 static, 1 kinematic, 2 dynamic. Joints and bodies need a `Box3DWorld` ancestor. A joint with
  only `body_a` anchors to the world at the joint's position.
- Structure scenes are separate static bodies, so they can't ride on a moving body. Use model-only instances
  and child colliders instead.
- Sensors: `is_sensor = true`, `collision_layer = 1073741824`.

## Blender headless (`tools/blender/run.py`, bpy 4.5)
- **exec-ing another family script rebinds single-underscore globals** such as `_HERE`. `build_chains.py`
  exported into the wrong folder until `HERE` was recomputed from `__file__` after the exec.
- **The glTF exporter ignores Cycles modifiers**, and `export_merged_animation_name` doesn't exist in 4.5. So
  `salvage_lib` unrolls cycles to the clip length and renames the clip by rewriting the glb JSON.
- **Unrolling only whole-turn rotations should accumulate.** Accumulating locations made pulses drift.
- **Rebuilding a family rewrites every glb** with small binary differences, even for unchanged models.
  Revert the untouched ones (`git checkout -- <glb>`) or use `--only` to avoid churn.
- **Additive field or glass materials render opaque in the Blender previews.** Hide them (`--hide Heat,Glass`) to
  see inside.

## Review renders (`tools/structures/view_hall.py`)
- The viewer must clear animation data on imported objects, or keyframes snap parts back to the model origin.
- `ROOM_ANGLE=<deg>` shows the Tumbler turned. `top`, `lab` and `wing` are preset views, or pass `cam:target`.
- The viewer reads the wing scenes and their MultiMeshes, importing each .glb once and copying it, so a
  wing renders in about 30 s (it used to re-import every instance and took 10+ minutes).
- Billboard labels come out mirrored in these renders but face the camera in Godot.

## Environment
- There's no Godot or .NET in the cloud container, so C# can't be compiled or scenes run. Say so in PRs.
- `excalidraw.com` is blocked by the network policy.
- Use `pip install bpy==4.5.14` for Blender, and Pillow for contact sheets.

## Decor and kit
- Decor props put their origin on the floor, not at a cell centre, and their fronts face Blender -Y (Godot +Z).
  Kit pieces use a 4 m module.
- `gen_decor.py` builds bounding-box colliders from glb node translation and scale, ignoring rotation. Any
  prop whose bounds would make a bad collider (a tree canopy, a catwalk, a hollow tower) needs hand-set boxes in `CUSTOM`/`KIT`.
- A Box3DBody's own shape is always centred on its origin, so colliders offset from the root are separate
  child bodies.

## Performance and scene size
- **Godot warns when a text scene gets large** (FileSystem > On Save > Warn on Saving Large Text Resources);
  ObjectMuseum.tscn hit 790 KB with ~1,800 MeshInstance3D boxes. The fixes, in place_museum.py:
  - **Batch boxes into MultiMeshes** (`Hall.batch`): one MultiMeshInstance3D per material per exhibit group.
    The tscn stores 12 floats per box, and the draw costs one call per MultiMesh. A MultiMesh buffer is row-major 3x4
    (`basis row, origin` x3), the same order as Transform3D text.
  - **Colliders don't need meshes**: a static Box3DBody alone collides, and its look comes from the MultiMesh.
  - **Split big generated levels into sub-scenes** (one per wing), instanced from the level. Keep
    NodePaths relative (`../Body`) so they survive the split.
  - Boxes on a moving body go in a MultiMesh *under that body* so they move with it.
  - Don't embed ArrayMesh data in a level `.tscn` (TestFacility.tscn is 39 MB from 128 inline ArrayMeshes).
    Instance a .glb or save the mesh as a binary `.res`.
- **Models (salvage_lib)**: static parts are merged into one `Body` node on export (`merge_static`). Nodes stay
  separate if they animate, if a scene addresses them (names are found by scanning `game/**/*.tscn|cs`
  for `parent="Model/..."`), if they use belt/glass/field materials, or if they're listed in `EXPORT_KEEP`.
  Each material left on a node is one draw call. **After a model rebuild, rerun the scene generators**:
  `index=` overrides on model children are computed from the glb's node order.
- **Realism for cheap**: `Builder.box` chamfers edges (`BEVEL`, 12 mm by default; `bevel=0` for faces that butt
  against neighbours, like kit seams). Chamfers are painted toward `C_WEAR` for worn edges. Face-area
  WeightedNormal keeps the big faces flat-shaded. Vertex colours are per face, so glTF splits vertices at every
  face anyway: expect about 2 verts per tri.
- Godot generates LODs and shadow meshes on import (`meshes/generate_lods=true`), so don't hand-author LODs.
- **Budgets used in the 2026-09 detail pass**: items ≤ 700 tris, 1 node and ≤ 2 surfaces (hundreds spawn);
  machines ≤ 8k; big decor/superstructures ≤ 15k; kit pieces lean (they tile hundreds of times). Skip the
  chamfer on glow strips, sheet under ~3 cm and parts under ~8 cm, and make foliage, paper, cracks and stains
  single flat polygons (the exported materials are double-sided).
- **Glass in a mesh switches off that whole node's shadow** (the import script sets `cast_shadow` per
  MeshInstance). Keep glass and field shells in their own nodes.
- **Detail helpers are still per family** (`stud`, `decal`, `streak`, `bolt_circle`, `grille`, `lean_path`,
  `band`, `lump`...). Promote them into salvage_lib when a third family needs one. Also open: a chamfer that
  scales with box size (12 mm is invisible on 20 m superstructure members but still costs 44 tris per box).
