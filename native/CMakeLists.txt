cmake_minimum_required(VERSION 3.22)

# Our own GDExtension binding of Box3D (native/thirdparty/box3d), built against godot-cpp.
# The library lands in addons/gmp_box3d/bin, where addons/gmp_box3d/gmp_box3d.gdextension loads it.
project(gmp_box3d LANGUAGES C CXX)

# Static CRT for everything, matching what Box3D's own CMakeLists asks for, so godot-cpp,
# Box3D and our code all link against the same runtime.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>" CACHE STRING "" FORCE)

# godot-cpp: bindings generated for the Godot version we run (4.7), with the bookkeeping
# that lets the editor hot reload the library when it is rebuilt.
set(GODOTCPP_API_VERSION "4.7" CACHE STRING "" FORCE)
set(GODOTCPP_TARGET "template_debug" CACHE STRING "" FORCE)
set(GODOTCPP_USE_HOT_RELOAD ON CACHE BOOL "" FORCE)
add_subdirectory(thirdparty/godot-cpp)

# Box3D as a static library. Its samples, tests and docs only build when it is the top
# level project, so add_subdirectory brings in just the library. On Linux a static library
# linked into our shared one must be built position independent (-fPIC).
set(BUILD_SHARED_LIBS OFF)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
add_subdirectory(thirdparty/box3d)

add_library(gmp_box3d SHARED
    src/register_types.cpp
    src/body_user_data.h
    src/math_convert.h
    src/physics_body.h
    src/physics_body.cpp
    src/physics_shape.h
    src/physics_shape.cpp
    src/physics_character.h
    src/physics_character.cpp
    src/physics_world.h
    src/physics_world.cpp
    src/physics_world_kinds.cpp
    src/physics_world_items.cpp
    src/physics_world_events.cpp
    src/physics_world_queries.cpp
    src/physics_world_sync.cpp
    src/item_state.h
    src/physics_world_render.cpp
)

target_link_libraries(gmp_box3d PRIVATE godot-cpp box3d)

get_target_property(GODOTCPP_SUFFIX godot-cpp GODOTCPP_SUFFIX)
set(OUTPUT_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../addons/gmp_box3d/bin")

# The $<1:...> wrappers stop the Visual Studio generator adding a per-config subfolder.
set_target_properties(gmp_box3d PROPERTIES
    CXX_STANDARD 17
    CXX_EXTENSIONS OFF
    POSITION_INDEPENDENT_CODE ON
    LIBRARY_OUTPUT_DIRECTORY "$<1:${OUTPUT_DIR}>"
    RUNTIME_OUTPUT_DIRECTORY "$<1:${OUTPUT_DIR}>"
    PDB_OUTPUT_DIRECTORY "$<1:${OUTPUT_DIR}>"
    PREFIX "lib"
    OUTPUT_NAME "gmp_box3d${GODOTCPP_SUFFIX}"
)

if(WIN32)
    add_custom_command(TARGET gmp_box3d PRE_LINK
        COMMAND ${CMAKE_COMMAND} -DDLL=$<TARGET_FILE:gmp_box3d> -P "${CMAKE_CURRENT_SOURCE_DIR}/cmake/move_locked_dll.cmake"
    )
endif()
