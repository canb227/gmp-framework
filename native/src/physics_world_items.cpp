// GMPPhysicsWorld: spawning, despawning and tracking items. See physics_world.h.

#include "physics_world.h"

#include "body_user_data.h"
#include "math_convert.h"

#include <godot_cpp/variant/utility_functions.hpp>

#include <cstdlib>

using namespace godot;

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

	int slot;
	if (!freeSlots.empty()) {
		slot = freeSlots.back();
		freeSlots.pop_back();
	} else {
		slot = (int)items.size();
		items.push_back(Item());
	}

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
	bodyDef.userData = user_data_for_item(slot);
	b3BodyId body = b3CreateBody(world, &bodyDef);

	b3ShapeDef shapeDef = b3DefaultShapeDef();
	shapeDef.density = kind.density;
	shapeDef.baseMaterial.friction = kind.friction;
	shapeDef.baseMaterial.restitution = kind.restitution;
	shapeDef.baseMaterial.rollingResistance = kind.rollingResistance;
	shapeDef.baseMaterial.userMaterialId = kind.userMaterialId;
	// Contact begin/end events cost per contact; items will turn them on only where a reaction needs them.
	shapeDef.enableContactEvents = false;
	// Machines' sensors see items, and items' impacts make sounds.
	shapeDef.enableSensorEvents = true;
	shapeDef.enableHitEvents = true;
	shapeDef.filter.categoryBits = itemLayer;
	b3ShapeId shape;
	if (kind.shape == ITEM_SHAPE_BOX) {
		shape = b3CreateHullShape(body, &shapeDef, &kind.box.base);
	} else if (kind.shape == ITEM_SHAPE_SPHERE) {
		shape = b3CreateSphereShape(body, &shapeDef, &kind.sphere);
	} else if (kind.shape == ITEM_SHAPE_CAPSULE) {
		shape = b3CreateCapsuleShape(body, &shapeDef, &kind.capsule);
	} else {
		shape = b3CreateHullShape(body, &shapeDef, kind.hull);
	}

	Item &item = items[slot];
	item.id = (uint64_t)id;
	item.kind = kindIndex;
	item.body = body;
	item.shape = shape;
	item.authority = (uint64_t)authority;
	// Spawned without its scale: bodies are placed by position and rotation only.
	item.position = transform.origin;
	item.rotation = transform.basis.get_rotation_quaternion();
	item.current = Transform3D(Basis(item.rotation), item.position);
	item.previous = item.current;
	item.movedTick = tick;
	item.asleep = false;
	item.indexInKind = (int)kind.slots.size();
	kind.slots.push_back(slot);
	kind.changedTick = tick;

	slotById[item.id] = slot;
	itemCount++;
}

void GMPPhysicsWorld::despawn_item(int64_t id) {
	auto found = slotById.find((uint64_t)id);
	if (found == slotById.end()) {
		return;
	}
	remove_item(found->second);
}

void GMPPhysicsWorld::clear_items() {
	for (int slot = 0; slot < (int)items.size(); slot++) {
		if (items[slot].id != 0) {
			remove_item(slot);
		}
	}
}

void GMPPhysicsWorld::remove_item(int slot) {
	Item &item = items[slot];
	ItemKind &kind = kinds[item.kind];

	// Take it out of its kind's draw list by moving the last one into its place.
	int lastSlot = kind.slots.back();
	kind.slots[item.indexInKind] = lastSlot;
	items[lastSlot].indexInKind = item.indexInKind;
	kind.slots.pop_back();
	kind.changedTick = tick;

	if (item.inFollowers) {
		for (int i = 0; i < (int)followers.size(); i++) {
			if (followers[i] == slot) {
				followers[i] = followers.back();
				followers.pop_back();
				break;
			}
		}
	}
	if (b3World_IsValid(world)) {
		b3DestroyBody(item.body);
	}
	slotById.erase(item.id);
	item = Item();
	freeSlots.push_back(slot);
	itemCount--;
}

int GMPPhysicsWorld::get_item_count() const {
	return itemCount;
}

// ---- one item ----

bool GMPPhysicsWorld::has_item(int64_t id) const {
	return slotById.count((uint64_t)id) > 0;
}

Transform3D GMPPhysicsWorld::get_item_transform(int64_t id) const {
	auto found = slotById.find((uint64_t)id);
	if (found == slotById.end()) {
		return Transform3D();
	}
	return items[found->second].current;
}

Vector3 GMPPhysicsWorld::get_item_linear_velocity(int64_t id) const {
	auto found = slotById.find((uint64_t)id);
	if (found == slotById.end()) {
		return Vector3();
	}
	return to_godot(b3Body_GetLinearVelocity(items[found->second].body));
}

Vector3 GMPPhysicsWorld::get_item_angular_velocity(int64_t id) const {
	auto found = slotById.find((uint64_t)id);
	if (found == slotById.end()) {
		return Vector3();
	}
	return to_godot(b3Body_GetAngularVelocity(items[found->second].body));
}

float GMPPhysicsWorld::get_item_mass(int64_t id) const {
	auto found = slotById.find((uint64_t)id);
	if (found == slotById.end()) {
		return 0;
	}
	return b3Body_GetMass(items[found->second].body);
}

bool GMPPhysicsWorld::is_item_asleep(int64_t id) const {
	auto found = slotById.find((uint64_t)id);
	if (found == slotById.end()) {
		return false;
	}
	return items[found->second].asleep;
}

void GMPPhysicsWorld::apply_item_force(int64_t id, Vector3 force) {
	auto found = slotById.find((uint64_t)id);
	if (found != slotById.end()) {
		b3Body_ApplyForceToCenter(items[found->second].body, to_b3(force), true);
	}
}

void GMPPhysicsWorld::apply_item_impulse(int64_t id, Vector3 impulse) {
	auto found = slotById.find((uint64_t)id);
	if (found != slotById.end()) {
		b3Body_ApplyLinearImpulseToCenter(items[found->second].body, to_b3(impulse), true);
	}
}

void GMPPhysicsWorld::damp_item_spin(int64_t id, float fraction) {
	auto found = slotById.find((uint64_t)id);
	if (found == slotById.end()) {
		return;
	}
	b3BodyId body = items[found->second].body;
	b3Vec3 spin = b3Body_GetAngularVelocity(body);
	float keep = 1.0f - fraction;
	b3Body_SetAngularVelocity(body, b3Vec3{ spin.x * keep, spin.y * keep, spin.z * keep });
}

void GMPPhysicsWorld::teleport_item(int64_t id, Transform3D transform) {
	auto found = slotById.find((uint64_t)id);
	if (found == slotById.end()) {
		return;
	}
	Item &item = items[found->second];
	Quaternion rotation = transform.basis.get_rotation_quaternion();
	b3Body_SetTransform(item.body, to_b3(transform.origin), to_b3(rotation));
	b3Body_SetAwake(item.body, true);
	// Drawn there from now, without sliding across.
	item.position = transform.origin;
	item.rotation = rotation;
	item.current = Transform3D(Basis(rotation), transform.origin);
	item.previous = item.current;
	item.movedTick = tick;
	kinds[item.kind].changedTick = tick;
}

void GMPPhysicsWorld::set_item_reports_touches(int64_t id, bool reports) {
	auto found = slotById.find((uint64_t)id);
	if (found != slotById.end()) {
		b3Shape_EnableContactEvents(items[found->second].shape, reports);
	}
}

PackedInt64Array GMPPhysicsWorld::get_items_below(float y) const {
	PackedInt64Array result;
	for (const Item &item : items) {
		if (item.id != 0 && item.authority == selfPeer && item.position.y < y) {
			result.push_back((int64_t)item.id);
		}
	}
	return result;
}

uint64_t GMPPhysicsWorld::item_id_at_slot(int slot) const {
	if (slot < 0 || slot >= (int)items.size()) {
		return 0;
	}
	return items[slot].id;
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
		int slot = std::rand() % (int)items.size();
		// Only what this peer simulates: followers go where their authority says.
		if (items[slot].id == 0 || items[slot].authority != selfPeer) {
			continue;
		}
		// The same kick as the stir console command gives the old bodies: mostly up, a little sideways.
		b3BodyId body = items[slot].body;
		b3Vec3 velocity = b3Body_GetLinearVelocity(body);
		velocity.x += random_range(-1.5f, 1.5f);
		velocity.y += random_range(2.0f, 3.5f);
		velocity.z += random_range(-1.5f, 1.5f);
		b3Body_SetLinearVelocity(body, velocity);
		b3Body_SetAwake(body, true);
	}
}
