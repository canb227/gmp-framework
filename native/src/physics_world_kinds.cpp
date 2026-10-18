// GMPPhysicsWorld: item kinds. See physics_world.h.

#include "physics_world.h"

#include "math_convert.h"

#include <godot_cpp/variant/utility_functions.hpp>

using namespace godot;

// Reads one value from a kind description, or the fallback when it isn't there.
static Variant read(const Dictionary &description, const char *key, const Variant &fallback) {
	if (description.has(key)) {
		return description[key];
	}
	return fallback;
}

// Adds a kind from C#'s description of an item scene (game/scripts/items/ItemKinds.cs builds it), with the
// same names as GMPShape's properties:
//   shape           0 box, 1 sphere, 2 capsule (along Y), 3 cylinder (along Y), 4 hull
//   size            Vector3, full size of the box
//   radius          float, of the sphere, capsule or cylinder
//   height          float, of the capsule or cylinder, the whole length
//   sides           int, of the cylinder
//   hull_points     PackedVector3Array, the points the hull wraps
//   density, friction, restitution, rolling_resistance   float
//   surface_type    int
//   cast_shadows    bool
//   base_priority   int, sync priority before bonuses; -1 never sends
//   parts           Array of Dictionary { mesh, transform, material_override, surface_materials (Array) }
// Returns the kind's index, which spawn_item takes, or -1 if the description can't be used.
int GMPPhysicsWorld::register_item_kind(Dictionary description) {
	ItemKind kind;
	int shapeType = read(description, "shape", 0);

	if (shapeType == ITEM_SHAPE_BOX) {
		Vector3 size = read(description, "size", Vector3(1, 1, 1));
		kind.shape = ITEM_SHAPE_BOX;
		kind.box = b3MakeBoxHull(size.x / 2, size.y / 2, size.z / 2);
	} else if (shapeType == ITEM_SHAPE_SPHERE) {
		kind.shape = ITEM_SHAPE_SPHERE;
		kind.sphere.center = b3Vec3{ 0, 0, 0 };
		kind.sphere.radius = read(description, "radius", 0.5f);
	} else if (shapeType == ITEM_SHAPE_CAPSULE) {
		float radius = read(description, "radius", 0.5f);
		float height = read(description, "height", 2.0f);
		// The two hemisphere centres, so the whole capsule is height long.
		float halfSegment = height / 2 - radius;
		if (halfSegment < 0) {
			halfSegment = 0;
		}
		kind.shape = ITEM_SHAPE_CAPSULE;
		kind.capsule.center1 = b3Vec3{ 0, -halfSegment, 0 };
		kind.capsule.center2 = b3Vec3{ 0, halfSegment, 0 };
		kind.capsule.radius = radius;
	} else if (shapeType == ITEM_SHAPE_CYLINDER) {
		float radius = read(description, "radius", 0.5f);
		float height = read(description, "height", 2.0f);
		int sides = read(description, "sides", 12);
		kind.shape = ITEM_SHAPE_CYLINDER;
		// Centred on the body: from -height/2 to +height/2.
		kind.hull = b3CreateCylinder(height, radius, -height / 2, sides);
	} else if (shapeType == ITEM_SHAPE_HULL) {
		PackedVector3Array points = read(description, "hull_points", PackedVector3Array());
		std::vector<b3Vec3> hullPoints;
		for (int i = 0; i < points.size(); i++) {
			hullPoints.push_back(to_b3(points[i]));
		}
		kind.shape = ITEM_SHAPE_HULL;
		if (!hullPoints.empty()) {
			kind.hull = b3CreateHull(hullPoints.data(), (int)hullPoints.size(), B3_MAX_HULL_VERTICES);
		}
	} else {
		ERR_PRINT(vformat("register_item_kind: shape %d can't be used on an item.", shapeType));
		return -1;
	}
	if ((kind.shape == ITEM_SHAPE_CYLINDER || kind.shape == ITEM_SHAPE_HULL) && kind.hull == nullptr) {
		ERR_PRINT("register_item_kind: couldn't make a hull for this item.");
		return -1;
	}

	kind.density = read(description, "density", 1000.0f);
	kind.friction = read(description, "friction", 0.6f);
	kind.restitution = read(description, "restitution", 0.0f);
	kind.rollingResistance = read(description, "rolling_resistance", 0.0f);
	kind.userMaterialId = (int64_t)read(description, "surface_type", 0);
	kind.castShadows = read(description, "cast_shadows", false);
	kind.basePriority = read(description, "base_priority", 1);

	Array parts = read(description, "parts", Array());
	for (int i = 0; i < parts.size(); i++) {
		Dictionary partDescription = parts[i];
		ItemPart part;
		part.mesh = read(partDescription, "mesh", Variant());
		if (part.mesh.is_null()) {
			continue;
		}
		part.offset = read(partDescription, "transform", Transform3D());
		part.materialOverride = read(partDescription, "material_override", Variant());
		Array surfaceMaterials = read(partDescription, "surface_materials", Array());
		bool anySet = false;
		for (int surface = 0; surface < surfaceMaterials.size(); surface++) {
			Ref<Material> material = surfaceMaterials[surface];
			part.surfaceMaterials.push_back(material);
			if (material.is_valid()) {
				anySet = true;
			}
		}
		if (!anySet) {
			part.surfaceMaterials.clear();
		}
		kind.parts.push_back(part);
	}

	kinds.push_back(kind);
	return (int)kinds.size() - 1;
}

void GMPPhysicsWorld::free_kinds() {
	for (ItemKind &kind : kinds) {
		if (kind.hull != nullptr) {
			b3DestroyHull(kind.hull);
			kind.hull = nullptr;
		}
	}
	kinds.clear();
}
