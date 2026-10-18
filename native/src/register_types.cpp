#include <gdextension_interface.h>

#include <godot_cpp/core/class_db.hpp>
#include <godot_cpp/core/defs.hpp>
#include <godot_cpp/godot.hpp>

#include "physics_body.h"
#include "physics_character.h"
#include "physics_shape.h"
#include "physics_world.h"

#include <box3d/base.h>

#include <godot_cpp/variant/utility_functions.hpp>

using namespace godot;

// Box3D's own checks print to the console and stop the program; this puts them in Godot's error log
// instead (with the file and line in Box3D) and lets the game carry on.
static int box3d_assert(const char *condition, const char *fileName, int lineNumber) {
	UtilityFunctions::push_error(vformat("Box3D check failed: %s (%s:%d)", condition, fileName, lineNumber));
	return 0;
}

void initialize_gmp_box3d(ModuleInitializationLevel p_level) {
	if (p_level != MODULE_INITIALIZATION_LEVEL_SCENE) {
		return;
	}

	b3SetAssertFcn(box3d_assert);
	GDREGISTER_CLASS(GMPPhysicsWorld);
	GDREGISTER_CLASS(GMPBody);
	GDREGISTER_CLASS(GMPShape);
	GDREGISTER_CLASS(GMPCharacter);
}

void uninitialize_gmp_box3d(ModuleInitializationLevel p_level) {
	if (p_level != MODULE_INITIALIZATION_LEVEL_SCENE) {
		return;
	}
}

extern "C" {
GDExtensionBool GDE_EXPORT gmp_box3d_library_init(GDExtensionInterfaceGetProcAddress p_get_proc_address, GDExtensionClassLibraryPtr p_library, GDExtensionInitialization *r_initialization) {
	GDExtensionBinding::InitObject init_obj(p_get_proc_address, p_library, r_initialization);

	init_obj.register_initializer(initialize_gmp_box3d);
	init_obj.register_terminator(uninitialize_gmp_box3d);
	init_obj.set_minimum_library_initialization_level(MODULE_INITIALIZATION_LEVEL_SCENE);

	return init_obj.init();
}
}
