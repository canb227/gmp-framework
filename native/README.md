# gmp_box3d

Our own GDExtension binding of [Box3D](https://github.com/erincatto/box3d): all of the game's physics.
Godot loads it through `addons/gmp_box3d/gmp_box3d.gdextension`; this folder has a `.gdignore` so Godot doesn't scan it.

- `thirdparty/box3d`: Erin Catto's Box3D (git submodule), built as a static library.
- `thirdparty/godot-cpp`: godot-cpp (git submodule), bindings generated for Godot 4.7.
- `src/`: our code. `register_types.cpp` registers the classes:
  - `GMPPhysicsWorld` (`physics_world*.cpp`): the Box3D world, loose items as plain data drawn through multimeshes,
    events and queries. The file list is at the top of `physics_world.h`.
  - `GMPBody` (`physics_body.*`): a body node for structures, players, tools and level geometry.
  - `GMPShape` (`physics_shape.*`): one collider (or sensor) of the body above it.

## Building

Needs CMake and Visual Studio 2026 (C++ workload). After cloning: `git submodule update --init`.

```bash
cmake -S native -B native/build -G "Visual Studio 18 2026" -A x64
cmake --build native/build --config RelWithDebInfo --target gmp_box3d
```

The first build takes about a minute (godot-cpp); after that only what changed. RelWithDebInfo is optimized with
Box3D's asserts on and symbols; use `--config Debug` to step through. The dll and pdb land in `addons/gmp_box3d/bin`.

New source files go in `add_library` in `CMakeLists.txt`.

## Reloading

- **Editor**: the extension is `reloadable`. The editor loads a `~` copy of the dll, so rebuild while it's open
  and it reloads the library when its window gets focus again.
- **A running game** (F5, the test harness) keeps the dll open and can't reload it. The build moves the open
  dll aside (`*.old`, cleaned up on a later build), so building still works; restart the game to pick it up.

`box3d_probe` in the in-game console checks the extension loaded.
