// GMPPhysicsWorld: the bodies. Item kinds, spawning loose items, node bodies joining the table, and one body by
// id. See physics_world.h.

#include "physics_world.h"

#include "math_convert.h"
#include "physics_body.h"
#include "tags.h"

#include <godot_cpp/variant/utility_functions.hpp>

#include <cstdlib>
#include <vector>

using namespace godot;

// Reads one value from a kind description, or the fallback when it isn't there.
static Variant read(const Dictionary &description, const char *key, const Variant &fallback) {
	if (description.has(key)) {
		return description[key];
	}
	return fallback;
}

// ---- item kinds ----

// Adds a kind from C#'s description of an item scene (ItemWorld.DescribeKind in game/scripts/items/ItemWorld.cs builds it):
//   shapes          Array of Dictionary, one per collider, with the same names as GMPShape's properties:
//     shape           0 box, 1 sphere, 2 capsule (along Y), 3 cylinder (along Y), 4 hull
//     transform       Transform3D, where it sits in the item (scale included)
//     size            Vector3, full size of the box
//     radius          float, of the sphere, capsule or cylinder
//     height          float, of the capsule or cylinder, the whole length
//     sides           int, of the cylinder
//     hull_points     PackedVector3Array, the points the hull wraps, in the collider's own axes
//     max_hull_vertices  int, at most this many hull corners (4 to 128)
//     density, friction, restitution, rolling_resistance   float
//     tags            int, the collider's own tags
//   tags            int, the tags the whole item starts with (0: none of its own)
//   cast_shadows    bool
//   base_priority   int, sync priority before bonuses; -1 never sends
//   parts           Array of Dictionary { mesh, transform, material_override, surface_materials (Array) }
// Returns the kind's index, which spawn_item takes, or -1 if the description can't be used.
int GMPPhysicsWorld::register_item_kind(Dictionary description) {
	ItemKind kind;

	Array shapes = read(description, "shapes", Array());
	for (int i = 0; i < shapes.size(); i++) {
		Dictionary shapeDescription = shapes[i];
		ShapeGeometry geometry;
		geometry.type = read(shapeDescription, "shape", 0);
		geometry.size = read(shapeDescription, "size", Vector3(1, 1, 1));
		geometry.radius = read(shapeDescription, "radius", 0.5f);
		geometry.height = read(shapeDescription, "height", 2.0f);
		geometry.sides = read(shapeDescription, "sides", 16);
		geometry.hullPoints = read(shapeDescription, "hull_points", PackedVector3Array());
		geometry.maxHullVertices = read(shapeDescription, "max_hull_vertices", B3_MAX_HULL_VERTICES);
		Transform3D local = read(shapeDescription, "transform", Transform3D());

		ItemKindShape kindShape;
		if (geometry.type == GMPShape::MESH || !place_shape(geometry, local, kindShape.placed)) {
			ERR_PRINT(vformat("register_item_kind: collider %d (shape %d) can't be made for an item.", i, geometry.type));
			free_placed_shape(kindShape.placed);
			continue;
		}
		kindShape.density = read(shapeDescription, "density", 1000.0f);
		kindShape.friction = read(shapeDescription, "friction", 0.6f);
		kindShape.restitution = read(shapeDescription, "restitution", 0.0f);
		kindShape.rollingResistance = read(shapeDescription, "rolling_resistance", 0.0f);
		kindShape.tags = (uint64_t)(int64_t)read(shapeDescription, "tags", 0);
		kind.shapes.push_back(kindShape);
	}
	if (kind.shapes.empty()) {
		ERR_PRINT("register_item_kind: the item has no collider that can be made.");
		return -1;
	}

	kind.tags = (uint64_t)(int64_t)read(description, "tags", 0);
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
		for (ItemKindShape &kindShape : kind.shapes) {
			free_placed_shape(kindShape.placed);
		}
	}
	kinds.clear();
}

// ---- the table ----

int GMPPhysicsWorld::new_slot() {
	if (!freeSlots.empty()) {
		int slot = freeSlots.back();
		freeSlots.pop_back();
		return slot;
	}
	bodies.push_back(Body());
	return (int)bodies.size() - 1;
}

Body *GMPPhysicsWorld::find_body(int64_t id) {
	auto found = slotById.find((uint64_t)id);
	if (found == slotById.end()) {
		return nullptr;
	}
	return &bodies[found->second];
}

Body *GMPPhysicsWorld::body_of_shape(b3ShapeId shapeId) {
	if (!b3Shape_IsValid(shapeId)) {
		return nullptr;
	}
	int slot = (int)(uintptr_t)b3Body_GetUserData(b3Shape_GetBody(shapeId)) - 1;
	if (slot < 0 || slot >= (int)bodies.size()) {
		return nullptr;
	}
	return &bodies[slot];
}

int32_t GMPPhysicsWorld::shape_index(b3ShapeId shapeId) {
	Body *body = body_of_shape(shapeId);
	if (body == nullptr) {
		return NoShapeIndex;
	}
	// An item collider's userData is its index in the kind, plus one; a node body's shape's is its GMPShape.
	if (body->kind >= 0) {
		return (int32_t)(uintptr_t)b3Shape_GetUserData(shapeId) - 1;
	}
	GMPShape *shape = (GMPShape *)b3Shape_GetUserData(shapeId);
	if (shape == nullptr) {
		return NoShapeIndex;
	}
	return shape->bodyShapeIndex;
}

// ---- loose items ----

void GMPPhysicsWorld::spawn_item(int64_t id, int kindIndex, int64_t authority, Transform3D transform, Vector3 linearVelocity, Vector3 angularVelocity) {
	if (!b3World_IsValid(world)) {
		ERR_PRINT("spawn_item: the world isn't in the tree yet.");
		return;
	}
	if (kindIndex < 0 || kindIndex >= (int)kinds.size()) {
		ERR_PRINT(vformat("spawn_item: there is no item kind %d.", kindIndex));
		return;
	}
	if (id == 0 || slotById.count((uint64_t)id) > 0) {
		ERR_PRINT(vformat("spawn_item: id %d is 0 or already in use.", id));
		return;
	}
	ItemKind &kind = kinds[kindIndex];
	int slot = new_slot();

	b3BodyDef bodyDef = b3DefaultBodyDef();
	// Simulated by its authority; everyone else drives it along the authority's states.
	if ((uint64_t)authority == selfPeer) {
		bodyDef.type = b3_dynamicBody;
	} else {
		bodyDef.type = b3_kinematicBody;
		linearVelocity = Vector3();
		angularVelocity = Vector3();
	}
	bodyDef.position = to_b3(transform.origin);
	bodyDef.rotation = to_b3(transform.basis.get_rotation_quaternion());
	bodyDef.linearVelocity = to_b3(linearVelocity);
	bodyDef.angularVelocity = to_b3(angularVelocity);
	bodyDef.userData = (void *)(uintptr_t)(slot + 1);
	b3BodyId body = b3CreateBody(world, &bodyDef);

	Body &item = bodies[slot];
	item.tags = kind.tags;
	for (int i = 0; i < (int)kind.shapes.size(); i++) {
		const ItemKindShape &kindShape = kind.shapes[i];
		b3ShapeDef shapeDef = b3DefaultShapeDef();
		shapeDef.density = kindShape.density;
		shapeDef.baseMaterial.friction = kindShape.friction;
		shapeDef.baseMaterial.restitution = kindShape.restitution;
		shapeDef.baseMaterial.rollingResistance = kindShape.rollingResistance;
		uint64_t tags = kindShape.tags | item.tags;
		shapeDef.baseMaterial.userMaterialId = tags;
		// Contact begin/end events cost per contact: only colliders a tag reaction watches report them.
		shapeDef.enableContactEvents = (tags & watcherTags) != 0;
		// Machines' sensors see items, and items' impacts make sounds.
		shapeDef.enableSensorEvents = true;
		shapeDef.enableHitEvents = true;
		shapeDef.filter.categoryBits = itemLayer;
		// Its index in the kind, plus one (so it is never null).
		shapeDef.userData = (void *)(uintptr_t)(i + 1);
		create_placed_shape(body, shapeDef, kindShape.placed);
	}

	item.id = (uint64_t)id;
	item.kind = kindIndex;
	item.body = body;
	item.authority = (uint64_t)authority;
	item.basePriority = kind.basePriority;
	// Spawned without its scale: bodies are placed by position and rotation only.
	item.position = transform.origin;
	item.rotation = transform.basis.get_rotation_quaternion();
	item.current = Transform3D(Basis(item.rotation), item.position);
	item.previous = item.current;
	item.movedTick = tick;
	item.indexInKind = (int)kind.slots.size();
	kind.slots.push_back(slot);
	kind.changedTick = tick;

	slotById[item.id] = slot;
	itemCount++;
}

void GMPPhysicsWorld::despawn_item(int64_t id) {
	auto found = slotById.find((uint64_t)id);
	if (found != slotById.end() && bodies[found->second].kind >= 0) {
		remove_item(found->second);
	}
}

void GMPPhysicsWorld::clear_items() {
	for (int slot = 0; slot < (int)bodies.size(); slot++) {
		if (bodies[slot].id != 0 && bodies[slot].kind >= 0) {
			remove_item(slot);
		}
	}
}

void GMPPhysicsWorld::remove_item(int slot) {
	Body &item = bodies[slot];
	ItemKind &kind = kinds[item.kind];

	// Take it out of its kind's draw list by moving the last one into its place.
	int lastSlot = kind.slots.back();
	kind.slots[item.indexInKind] = lastSlot;
	bodies[lastSlot].indexInKind = item.indexInKind;
	kind.slots.pop_back();
	kind.changedTick = tick;

	remove_from_followers(slot);
	if (b3World_IsValid(world)) {
		b3DestroyBody(item.body);
	}
	slotById.erase(item.id);
	item = Body();
	freeSlots.push_back(slot);
	itemCount--;
}

int GMPPhysicsWorld::get_item_count() const {
	return itemCount;
}

PackedInt64Array GMPPhysicsWorld::get_items_below(float y) const {
	PackedInt64Array result;
	for (const Body &body : bodies) {
		if (body.id != 0 && body.kind >= 0 && body.authority == selfPeer && body.position.y < y) {
			result.push_back((int64_t)body.id);
		}
	}
	return result;
}

void GMPPhysicsWorld::apply_item_tags(Body &item) {
	const ItemKind &kind = kinds[item.kind];
	std::vector<b3ShapeId> shapes(b3Body_GetShapeCount(item.body));
	int count = b3Body_GetShapes(item.body, shapes.data(), (int)shapes.size());
	for (int i = 0; i < count; i++) {
		int index = (int)(uintptr_t)b3Shape_GetUserData(shapes[i]) - 1;
		if (index < 0 || index >= (int)kind.shapes.size()) {
			continue;
		}
		uint64_t tags = kind.shapes[index].tags | item.tags;
		b3SurfaceMaterial material = b3Shape_GetSurfaceMaterial(shapes[i]);
		material.userMaterialId = tags;
		b3Shape_SetSurfaceMaterial(shapes[i], material);
		b3Shape_EnableContactEvents(shapes[i], (tags & watcherTags) != 0);
	}
}

// Random number from low to high.
static float random_range(float low, float high) {
	return low + (high - low) * (std::rand() / (float)RAND_MAX);
}

void GMPPhysicsWorld::kick_random_items(int count) {
	if (itemCount == 0) {
		return;
	}
	for (int i = 0; i < count; i++) {
		Body &item = bodies[std::rand() % (int)bodies.size()];
		// Only items this peer simulates: followers go where their authority says.
		if (item.id == 0 || item.kind < 0 || item.authority != selfPeer) {
			continue;
		}
		// Mostly up, a little sideways.
		b3Vec3 velocity = b3Body_GetLinearVelocity(item.body);
		velocity.x += random_range(-1.5f, 1.5f);
		velocity.y += random_range(2.0f, 3.5f);
		velocity.z += random_range(-1.5f, 1.5f);
		b3Body_SetLinearVelocity(item.body, velocity);
		b3Body_SetAwake(item.body, true);
	}
}

// ---- node bodies ----

int GMPPhysicsWorld::add_node_body(GMPBody *node) {
	int slot = new_slot();
	Body &body = bodies[slot];
	body.node = node;
	body.body = node->get_body_id();
	Transform3D transform = node->get_global_transform();
	body.position = transform.origin;
	body.rotation = transform.basis.get_rotation_quaternion();
	body.current = Transform3D(Basis(body.rotation), body.position);
	body.previous = body.current;
	set_body_id(slot, node->sharedId);
	return slot;
}

void GMPPhysicsWorld::remove_node_body(int slot) {
	if (slot < 0 || slot >= (int)bodies.size()) {
		return;
	}
	remove_from_followers(slot);
	set_body_id(slot, 0);
	bodies[slot] = Body();
	freeSlots.push_back(slot);
}

void GMPPhysicsWorld::remove_from_followers(int slot) {
	if (!bodies[slot].inFollowers) {
		return;
	}
	for (int i = 0; i < (int)followers.size(); i++) {
		if (followers[i] == slot) {
			followers[i] = followers.back();
			followers.pop_back();
			return;
		}
	}
}

void GMPPhysicsWorld::set_body_id(int slot, uint64_t id) {
	Body &body = bodies[slot];
	if (body.id != 0) {
		slotById.erase(body.id);
	}
	body.id = id;
	if (id != 0) {
		slotById[id] = slot;
	}
}

// ---- one body by id ----

bool GMPPhysicsWorld::has_body(int64_t id) const {
	return slotById.count((uint64_t)id) > 0;
}

Object *GMPPhysicsWorld::get_body_node(int64_t id) const {
	auto found = slotById.find((uint64_t)id);
	if (found == slotById.end()) {
		return nullptr;
	}
	return bodies[found->second].node;
}

Transform3D GMPPhysicsWorld::get_body_transform(int64_t id) const {
	auto found = slotById.find((uint64_t)id);
	if (found == slotById.end()) {
		return Transform3D();
	}
	return bodies[found->second].current;
}

Vector3 GMPPhysicsWorld::get_body_linear_velocity(int64_t id) const {
	auto found = slotById.find((uint64_t)id);
	if (found == slotById.end()) {
		return Vector3();
	}
	return to_godot(b3Body_GetLinearVelocity(bodies[found->second].body));
}

Vector3 GMPPhysicsWorld::get_body_angular_velocity(int64_t id) const {
	auto found = slotById.find((uint64_t)id);
	if (found == slotById.end()) {
		return Vector3();
	}
	return to_godot(b3Body_GetAngularVelocity(bodies[found->second].body));
}

float GMPPhysicsWorld::get_body_mass(int64_t id) const {
	auto found = slotById.find((uint64_t)id);
	if (found == slotById.end()) {
		return 0;
	}
	return b3Body_GetMass(bodies[found->second].body);
}

bool GMPPhysicsWorld::is_body_asleep(int64_t id) const {
	auto found = slotById.find((uint64_t)id);
	if (found == slotById.end()) {
		return false;
	}
	return bodies[found->second].asleep;
}

void GMPPhysicsWorld::apply_body_force(int64_t id, Vector3 force) {
	Body *body = find_body(id);
	if (body != nullptr) {
		b3Body_ApplyForceToCenter(body->body, to_b3(force), true);
	}
}

void GMPPhysicsWorld::apply_body_impulse(int64_t id, Vector3 impulse) {
	Body *body = find_body(id);
	if (body != nullptr) {
		b3Body_ApplyLinearImpulseToCenter(body->body, to_b3(impulse), true);
	}
}

void GMPPhysicsWorld::damp_body_spin(int64_t id, float fraction) {
	Body *body = find_body(id);
	if (body == nullptr) {
		return;
	}
	b3Vec3 spin = b3Body_GetAngularVelocity(body->body);
	float keep = 1.0f - fraction;
	b3Body_SetAngularVelocity(body->body, b3Vec3{ spin.x * keep, spin.y * keep, spin.z * keep });
}

void GMPPhysicsWorld::teleport_body(int64_t id, Transform3D transform) {
	Body *body = find_body(id);
	if (body == nullptr) {
		return;
	}
	Quaternion rotation = transform.basis.get_rotation_quaternion();
	b3Body_SetTransform(body->body, to_b3(transform.origin), to_b3(rotation));
	b3Body_SetAwake(body->body, true);
	set_pose(*body, transform.origin, rotation);
	// Drawn there from now, without sliding across.
	body->previous = body->current;
}

void GMPPhysicsWorld::set_body_tags(int64_t id, int64_t tags) {
	Body *body = find_body(id);
	if (body == nullptr) {
		return;
	}
	if (body->node != nullptr) {
		body->node->set_tags(tags);
		return;
	}
	body->tags = (uint64_t)tags;
	apply_item_tags(*body);
}

void GMPPhysicsWorld::set_pose(Body &body, Vector3 position, Quaternion rotation) {
	body.previous = body.current;
	body.position = position;
	body.rotation = rotation;
	body.current = Transform3D(Basis(rotation), position);
	body.movedTick = tick;
	if (body.kind >= 0) {
		kinds[body.kind].changedTick = tick;
	}
	if (body.node != nullptr) {
		body.node->place_node(b3WorldTransform{ to_b3(position), to_b3(rotation) });
	}
}
