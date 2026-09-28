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
- Whole-museum renders now take more than 10 minutes. Run them in the background.
- Billboard labels come out mirrored in these renders but face the camera in Godot.

## Environment
- There's no Godot or .NET in the cloud container, so C# can't be compiled or scenes run. Say so in PRs.
- `excalidraw.com` is blocked by the network policy.
- Use `pip install bpy==4.5.14` for Blender, and Pillow for contact sheets.
