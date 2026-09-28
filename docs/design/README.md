# Design notes: physics-automation puzzles

Hard-won lessons from building the museum's concept structures, resources and puzzle rooms. Read the file that
matches your task before starting; each is short and self-contained.

| File | Read it when you are... |
|---|---|
| [principles.md](principles.md) | designing anything: the core rules for what makes a physics-automation challenge good |
| [space-age-lessons.md](space-age-lessons.md) | inventing a new theme, planet-like area or chain: what Factorio Space Age (and its modded planets) teach, and how each lesson translates to physical items |
| [production-chains.md](production-chains.md) | designing a resource → product chain around an item tag: the recipe, a worked template and the two shipped chains |
| [puzzle-rooms.md](puzzle-rooms.md) | building a walk-in puzzle room: what a room needs, the six shipped rooms and what each taught |
| [physics-cookbook.md](physics-cookbook.md) | tuning numbers: friction, slope, spin, density and hinge thresholds that make a behaviour happen (or not) |
| [pipeline-gotchas.md](pipeline-gotchas.md) | touching the generators, Blender scripts, .tscn/.import files or Box3D nodes: traps that cost time |

Where the actual content lives:
- Items (physics, tags, text): `tools/items/gen_items.py`, which also generates `tools/items/README.md`.
- Structures, museum and rooms: `tools/structures/` (see its README) and `tools/blender/run.py` (model families).
- Tag rules: `game/scripts/items/ItemTags.cs` and `game/scripts/items/TagInteractions.cs`.
- Room motion: `game/scripts/entities/RotatingRoom.cs`.

Status note (as of this writing): nothing here has been verified in Godot. Tag rules only log, there are no
recipes, and puzzle-room products are frozen display models. Physics properties and colliders are real.
