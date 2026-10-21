#include "physics_body.h"

#include "math_convert.h"
#include "physics_shape.h"
#include "physics_world.h"
#include "tags.h"

#include <godot_cpp/core/class_db.hpp>

using namespace godot;

GMPBody::GMPBody() {
}

GMPBody::~GMPBody() {
	destroy_body();
}

void GMPBody::_notification(int what) {
	if (what == NOTIFICATION_READY) {
		create_body();
	}
	if (what == NOTIFICATION_ENTER_TREE && is_node_ready()) {
		// Back in the tree after leaving it (READY only comes once).
		create_body();
	}
	if (what == NOTIFICATION_EXIT_TREE) {
		destroy_body();
	}
}

void GMPBody::create_body() {
	destroy_body();
	if (!enabled) {
		return;
	}
	GMPPhysicsWorld *world = GMPPhysicsWorld::get_current();
	if (world == nullptr || !b3World_IsValid(world->get_world_id())) {
		ERR_PRINT(vformat("%s: there is no physics world for this body.", get_path()));
		return;
	}

	Transform3D transform = get_global_transform();
	nodeScale = transform.basis.get_scale();
	lastTarget = transform;
	wasMoving = false;

	b3BodyDef def = b3DefaultBodyDef();
	if (bodyType == STATIC) {
		def.type = b3_staticBody;
	} else if (bodyType == KINEMATIC) {
		def.type = b3_kinematicBody;
	} else {
		def.type = b3_dynamicBody;
	}
	def.position = to_b3(transform.origin);
	def.rotation = to_b3(transform.basis.get_rotation_quaternion());
	def.gravityScale = gravityScale;
	def.linearDamping = linearDamping;
	def.angularDamping = angularDamping;
	def.enableSleep = canSleep;
	bodyId = b3CreateBody(world->get_world_id(), &def);
	// Its slot in the world's table, plus one, tells events and queries which body it is.
	slot = world->add_node_body(this);
	b3Body_SetUserData(bodyId, (void *)(uintptr_t)(slot + 1));
	attach_shapes();
}

void GMPBody::destroy_body() {
	if (!b3Body_IsValid(bodyId)) {
		bodyId = b3_nullBodyId;
		return;
	}
	std::vector<GMPShape *> shapes;
	collect_shapes(this, shapes);
	for (GMPShape *shape : shapes) {
		shape->detach();
	}
	b3DestroyBody(bodyId);
	bodyId = b3_nullBodyId;
	GMPPhysicsWorld *world = GMPPhysicsWorld::get_current();
	if (world != nullptr) {
		world->remove_node_body(slot);
	}
	slot = -1;
}

void GMPBody::forget_body() {
	bodyId = b3_nullBodyId;
	slot = -1;
	std::vector<GMPShape *> shapes;
	collect_shapes(this, shapes);
	for (GMPShape *shape : shapes) {
		shape->detach();
	}
}

void GMPBody::collect_shapes(Node *node, std::vector<GMPShape *> &shapes) {
	for (int i = 0; i < node->get_child_count(); i++) {
		Node *child = node->get_child(i);
		// A body under this one owns the shapes under it.
		if (Object::cast_to<GMPBody>(child) != nullptr) {
			continue;
		}
		GMPShape *shape = Object::cast_to<GMPShape>(child);
		if (shape != nullptr) {
			shapes.push_back(shape);
		}
		collect_shapes(child, shapes);
	}
}

void GMPBody::rebuild_shapes() {
	if (has_body()) {
		attach_shapes();
	}
}

void GMPBody::attach_shapes() {
	std::vector<GMPShape *> shapes;
	collect_shapes(this, shapes);
	for (int i = 0; i < (int)shapes.size(); i++) {
		// Numbered in tree order, the same on every peer, so a hit can name the shape.
		shapes[i]->bodyShapeIndex = i;
		shapes[i]->attach(this);
	}
}

b3BodyId GMPBody::get_body_id() const {
	return bodyId;
}

bool GMPBody::has_body() const {
	return b3Body_IsValid(bodyId);
}

// ---- moving it ----

void GMPBody::teleport(Transform3D transform) {
	set_global_transform(transform);
	lastTarget = transform;
	if (has_body()) {
		b3Body_SetTransform(bodyId, to_b3(transform.origin), to_b3(transform.basis.get_rotation_quaternion()));
	}
}

Vector3 GMPBody::get_linear_velocity() const {
	if (!has_body()) {
		return Vector3();
	}
	return to_godot(b3Body_GetLinearVelocity(bodyId));
}

void GMPBody::set_linear_velocity(Vector3 velocity) {
	if (has_body()) {
		b3Body_SetLinearVelocity(bodyId, to_b3(velocity));
	}
}

Vector3 GMPBody::get_angular_velocity() const {
	if (!has_body()) {
		return Vector3();
	}
	return to_godot(b3Body_GetAngularVelocity(bodyId));
}

void GMPBody::set_angular_velocity(Vector3 velocity) {
	if (has_body()) {
		b3Body_SetAngularVelocity(bodyId, to_b3(velocity));
	}
}

void GMPBody::apply_central_impulse(Vector3 impulse) {
	if (has_body()) {
		b3Body_ApplyLinearImpulseToCenter(bodyId, to_b3(impulse), true);
	}
}

void GMPBody::apply_impulse(Vector3 impulse, Vector3 point) {
	if (has_body()) {
		b3Body_ApplyLinearImpulse(bodyId, to_b3(impulse), to_b3(point), true);
	}
}

void GMPBody::apply_central_force(Vector3 force) {
	if (has_body()) {
		b3Body_ApplyForceToCenter(bodyId, to_b3(force), true);
	}
}

void GMPBody::apply_angular_impulse(Vector3 impulse) {
	if (has_body()) {
		b3Body_ApplyAngularImpulse(bodyId, to_b3(impulse), true);
	}
}

float GMPBody::get_mass() const {
	if (!has_body()) {
		return 0;
	}
	return b3Body_GetMass(bodyId);
}

bool GMPBody::is_awake() const {
	if (!has_body()) {
		return false;
	}
	return b3Body_IsAwake(bodyId);
}

void GMPBody::set_awake(bool awake) {
	if (has_body()) {
		b3Body_SetAwake(bodyId, awake);
	}
}

// ---- each tick ----

void GMPBody::follow_node(float delta) {
	Transform3D target = get_global_transform();
	bool moving = !target.is_equal_approx(lastTarget);
	// One more call after the node stops, at its resting transform: that brings the body's velocity to zero.
	if (moving || wasMoving) {
		b3WorldTransform worldTarget = { to_b3(target.origin), to_b3(target.basis.get_rotation_quaternion()) };
		b3Body_SetTargetTransform(bodyId, worldTarget, delta, true);
	}
	lastTarget = target;
	wasMoving = moving;
}

void GMPBody::place_node(const b3WorldTransform &transform) {
	Transform3D placed = to_godot(transform);
	placed.basis.scale_local(nodeScale);
	set_global_transform(placed);
}

// ---- properties ----

void GMPBody::set_body_type(int value) {
	bodyType = (BodyType)value;
	if (!has_body()) {
		return;
	}
	if (bodyType == STATIC) {
		b3Body_SetType(bodyId, b3_staticBody);
	} else if (bodyType == KINEMATIC) {
		b3Body_SetType(bodyId, b3_kinematicBody);
		lastTarget = get_global_transform();
		wasMoving = false;
	} else {
		b3Body_SetType(bodyId, b3_dynamicBody);
	}
}

int GMPBody::get_body_type() const {
	return bodyType;
}

void GMPBody::set_gravity_scale(float value) {
	gravityScale = value;
	if (has_body()) {
		b3Body_SetGravityScale(bodyId, value);
	}
}

float GMPBody::get_gravity_scale() const {
	return gravityScale;
}

void GMPBody::set_linear_damping(float value) {
	linearDamping = value;
	if (has_body()) {
		b3Body_SetLinearDamping(bodyId, value);
	}
}

float GMPBody::get_linear_damping() const {
	return linearDamping;
}

void GMPBody::set_angular_damping(float value) {
	angularDamping = value;
	if (has_body()) {
		b3Body_SetAngularDamping(bodyId, value);
	}
}

float GMPBody::get_angular_damping() const {
	return angularDamping;
}

void GMPBody::set_can_sleep(bool value) {
	canSleep = value;
	if (has_body()) {
		b3Body_EnableSleep(bodyId, value);
	}
}

bool GMPBody::get_can_sleep() const {
	return canSleep;
}

void GMPBody::set_collision_layer(int64_t value) {
	collisionLayer = (uint32_t)value;
	rebuild_shapes();
}

int64_t GMPBody::get_collision_layer() const {
	return collisionLayer;
}

void GMPBody::set_collision_mask(int64_t value) {
	collisionMask = (uint32_t)value;
	rebuild_shapes();
}

int64_t GMPBody::get_collision_mask() const {
	return collisionMask;
}


void GMPBody::set_enabled(bool value) {
	enabled = value;
	if (!is_inside_tree() || !is_node_ready()) {
		return;
	}
	if (enabled) {
		create_body();
	} else {
		destroy_body();
	}
}

bool GMPBody::get_enabled() const {
	return enabled;
}

void GMPBody::set_tags(int64_t value) {
	tags = (uint64_t)value;
	rebuild_shapes();
}

int64_t GMPBody::get_tags() const {
	return (int64_t)tags;
}

void GMPBody::set_shared_id(int64_t value) {
	sharedId = (uint64_t)value;
	GMPPhysicsWorld *world = GMPPhysicsWorld::get_current();
	if (world != nullptr && slot >= 0) {
		world->set_body_id(slot, sharedId);
	}
}

int64_t GMPBody::get_shared_id() const {
	return (int64_t)sharedId;
}

int64_t GMPBody::get_all_tags() {
	uint64_t all = tags;
	std::vector<GMPShape *> shapes;
	collect_shapes(this, shapes);
	for (GMPShape *shape : shapes) {
		all |= shape->tags;
	}
	return (int64_t)all;
}

void GMPBody::_bind_methods() {
	BIND_ENUM_CONSTANT(STATIC);
	BIND_ENUM_CONSTANT(KINEMATIC);
	BIND_ENUM_CONSTANT(DYNAMIC);

	ClassDB::bind_method(D_METHOD("teleport", "transform"), &GMPBody::teleport);
	ClassDB::bind_method(D_METHOD("get_linear_velocity"), &GMPBody::get_linear_velocity);
	ClassDB::bind_method(D_METHOD("set_linear_velocity", "velocity"), &GMPBody::set_linear_velocity);
	ClassDB::bind_method(D_METHOD("get_angular_velocity"), &GMPBody::get_angular_velocity);
	ClassDB::bind_method(D_METHOD("set_angular_velocity", "velocity"), &GMPBody::set_angular_velocity);
	ClassDB::bind_method(D_METHOD("apply_central_impulse", "impulse"), &GMPBody::apply_central_impulse);
	ClassDB::bind_method(D_METHOD("apply_impulse", "impulse", "point"), &GMPBody::apply_impulse);
	ClassDB::bind_method(D_METHOD("apply_central_force", "force"), &GMPBody::apply_central_force);
	ClassDB::bind_method(D_METHOD("apply_angular_impulse", "impulse"), &GMPBody::apply_angular_impulse);
	ClassDB::bind_method(D_METHOD("get_mass"), &GMPBody::get_mass);
	ClassDB::bind_method(D_METHOD("is_awake"), &GMPBody::is_awake);
	ClassDB::bind_method(D_METHOD("set_awake", "awake"), &GMPBody::set_awake);

	ClassDB::bind_method(D_METHOD("set_body_type", "value"), &GMPBody::set_body_type);
	ClassDB::bind_method(D_METHOD("get_body_type"), &GMPBody::get_body_type);
	ClassDB::bind_method(D_METHOD("set_gravity_scale", "value"), &GMPBody::set_gravity_scale);
	ClassDB::bind_method(D_METHOD("get_gravity_scale"), &GMPBody::get_gravity_scale);
	ClassDB::bind_method(D_METHOD("set_linear_damping", "value"), &GMPBody::set_linear_damping);
	ClassDB::bind_method(D_METHOD("get_linear_damping"), &GMPBody::get_linear_damping);
	ClassDB::bind_method(D_METHOD("set_angular_damping", "value"), &GMPBody::set_angular_damping);
	ClassDB::bind_method(D_METHOD("get_angular_damping"), &GMPBody::get_angular_damping);
	ClassDB::bind_method(D_METHOD("set_can_sleep", "value"), &GMPBody::set_can_sleep);
	ClassDB::bind_method(D_METHOD("get_can_sleep"), &GMPBody::get_can_sleep);
	ClassDB::bind_method(D_METHOD("set_collision_layer", "value"), &GMPBody::set_collision_layer);
	ClassDB::bind_method(D_METHOD("get_collision_layer"), &GMPBody::get_collision_layer);
	ClassDB::bind_method(D_METHOD("set_collision_mask", "value"), &GMPBody::set_collision_mask);
	ClassDB::bind_method(D_METHOD("get_collision_mask"), &GMPBody::get_collision_mask);
	ClassDB::bind_method(D_METHOD("set_enabled", "value"), &GMPBody::set_enabled);
	ClassDB::bind_method(D_METHOD("get_enabled"), &GMPBody::get_enabled);
	ClassDB::bind_method(D_METHOD("set_tags", "value"), &GMPBody::set_tags);
	ClassDB::bind_method(D_METHOD("get_tags"), &GMPBody::get_tags);
	ClassDB::bind_method(D_METHOD("get_all_tags"), &GMPBody::get_all_tags);
	ClassDB::bind_method(D_METHOD("set_shared_id", "value"), &GMPBody::set_shared_id);
	ClassDB::bind_method(D_METHOD("get_shared_id"), &GMPBody::get_shared_id);

	ADD_PROPERTY(PropertyInfo(Variant::INT, "body_type", PROPERTY_HINT_ENUM, "Static,Kinematic,Dynamic"), "set_body_type", "get_body_type");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "gravity_scale"), "set_gravity_scale", "get_gravity_scale");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "linear_damping"), "set_linear_damping", "get_linear_damping");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "angular_damping"), "set_angular_damping", "get_angular_damping");
	ADD_PROPERTY(PropertyInfo(Variant::BOOL, "can_sleep"), "set_can_sleep", "get_can_sleep");
	ADD_PROPERTY(PropertyInfo(Variant::INT, "collision_layer", PROPERTY_HINT_LAYERS_3D_PHYSICS), "set_collision_layer", "get_collision_layer");
	ADD_PROPERTY(PropertyInfo(Variant::INT, "collision_mask", PROPERTY_HINT_LAYERS_3D_PHYSICS), "set_collision_mask", "get_collision_mask");
	ADD_PROPERTY(PropertyInfo(Variant::BOOL, "enabled"), "set_enabled", "get_enabled");
	ADD_PROPERTY(PropertyInfo(Variant::INT, "tags", PROPERTY_HINT_FLAGS, TagHintString), "set_tags", "get_tags");
	// Set by C# at spawn, never saved in a scene.
	ADD_PROPERTY(PropertyInfo(Variant::INT, "shared_id", PROPERTY_HINT_NONE, "", PROPERTY_USAGE_NONE), "set_shared_id", "get_shared_id");
}
