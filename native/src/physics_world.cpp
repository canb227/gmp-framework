#include "physics_world.h"

#include "math_convert.h"
#include "physics_body.h"
#include "tags.h"

#include <godot_cpp/core/class_db.hpp>

#include <chrono>

using namespace godot;

// Milliseconds since some fixed point, for timing the step.
static double now_ms() {
	return std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now().time_since_epoch()).count();
}

static GMPPhysicsWorld *currentWorld = nullptr;

GMPPhysicsWorld *GMPPhysicsWorld::get_current() {
	return currentWorld;
}

b3WorldId GMPPhysicsWorld::get_world_id() const {
	return world;
}

GMPPhysicsWorld::GMPPhysicsWorld() {
}

GMPPhysicsWorld::~GMPPhysicsWorld() {
	destroy_world();
	free_kinds();
}

void GMPPhysicsWorld::_notification(int what) {
	if (what == NOTIFICATION_ENTER_TREE) {
		create_world();
		currentWorld = this;
	}
	if (what == NOTIFICATION_EXIT_TREE) {
		// The multimeshes are drawn in this node's world, so they go when it leaves.
		free_rendering();
		if (currentWorld == this) {
			currentWorld = nullptr;
		}
	}
}

void GMPPhysicsWorld::create_world() {
	if (b3World_IsValid(world)) {
		return;
	}
	b3WorldDef def = b3DefaultWorldDef();
	def.gravity = to_b3(gravity);
	// Box3D's own task scheduler runs the solver on this many threads.
	def.workerCount = workerCount;
	def.hitEventThreshold = MinHitSpeed;
	world = b3CreateWorld(&def);
}

void GMPPhysicsWorld::destroy_world() {
	free_rendering();
	if (currentWorld == this) {
		currentWorld = nullptr;
	}
	// Bodies of nodes still around go with the world: tell them, so they don't use them again.
	for (Body &body : bodies) {
		if (body.node != nullptr) {
			body.node->forget_body();
		}
	}
	if (b3World_IsValid(world)) {
		// Destroys every body and shape in it too.
		b3DestroyWorld(world);
	}
	world = b3_nullWorldId;
	bodies.clear();
	followers.clear();
	freeSlots.clear();
	slotById.clear();
	itemCount = 0;
	for (ItemKind &kind : kinds) {
		kind.slots.clear();
	}
}

void GMPPhysicsWorld::step(float delta) {
	if (!b3World_IsValid(world)) {
		return;
	}
	tick++;

	// Followers are driven along their authorities' states.
	play_followers(delta);
	// Kinematic node bodies go where their nodes were put since the last step.
	for (Body &body : bodies) {
		if (body.node != nullptr && body.node->bodyType == GMPBody::KINEMATIC) {
			body.node->follow_node(delta);
		}
	}

	double start = now_ms();
	b3World_Step(world, delta, subSteps);
	double stepped = now_ms();
	// Moves first: they only update positions, while sensor and touch signals run game code that may
	// add or remove bodies.
	read_move_events();
	read_sensor_events();
	read_contact_events();
	double eventsRead = now_ms();
	update_rendering();
	double rendered = now_ms();

	stepMs = stepped - start;
	eventsMs = eventsRead - stepped;
	renderMs = rendered - eventsRead;
}

Dictionary GMPPhysicsWorld::get_stats() const {
	int awake = 0;
	if (b3World_IsValid(world)) {
		awake = b3World_GetAwakeBodyCount(world);
	}
	Dictionary stats;
	stats["items"] = itemCount;
	stats["awake"] = awake;
	stats["step_ms"] = stepMs;
	stats["events_ms"] = eventsMs;
	stats["render_ms"] = renderMs;
	stats["followers"] = (int64_t)followers.size();
	// Compares peers' copies: equal (to a few mm) once followers have caught up with every item.
	double positionSum = 0;
	for (const Body &body : bodies) {
		if (body.id != 0 && body.kind >= 0) {
			positionSum += body.position.x + body.position.y + body.position.z;
		}
	}
	stats["position_sum"] = positionSum;
	stats["states_built"] = statesBuilt;
	stats["states_applied"] = statesApplied;
	stats["build_ms_total"] = buildMsTotal;
	stats["apply_ms_total"] = applyMsTotal;
	stats["follow_ms_total"] = followMsTotal;
	stats["hits_sent"] = hitsSent;
	stats["hits_heard"] = hitsHeard;
	return stats;
}

void GMPPhysicsWorld::set_gravity(Vector3 value) {
	gravity = value;
	if (b3World_IsValid(world)) {
		b3World_SetGravity(world, to_b3(value));
	}
}

Vector3 GMPPhysicsWorld::get_gravity() const {
	return gravity;
}

void GMPPhysicsWorld::set_item_layer(int64_t value) {
	itemLayer = (uint32_t)value;
}

int64_t GMPPhysicsWorld::get_item_layer() const {
	return itemLayer;
}

void GMPPhysicsWorld::set_watcher_tags(int64_t value) {
	watcherTags = (uint64_t)value;
	// Items already in the world report their touches by the new tags from now on.
	for (Body &body : bodies) {
		if (body.id != 0 && body.kind >= 0) {
			apply_item_tags(body);
		}
	}
}

int64_t GMPPhysicsWorld::get_watcher_tags() const {
	return (int64_t)watcherTags;
}

Dictionary GMPPhysicsWorld::get_profile() const {
	Dictionary profile;
	if (!b3World_IsValid(world)) {
		return profile;
	}
	b3Profile p = b3World_GetProfile(world);
	profile["step"] = p.step;
	profile["pairs"] = p.pairs;
	profile["collide"] = p.collide;
	profile["solve"] = p.solve;
	profile["sensors"] = p.sensors;
	profile["hit_events"] = p.hitEvents;
	profile["sleep_islands"] = p.sleepIslands;
	b3Counters c = b3World_GetCounters(world);
	profile["bodies"] = c.bodyCount;
	profile["shapes"] = c.shapeCount;
	profile["contacts"] = c.contactCount;
	profile["awake"] = b3World_GetAwakeBodyCount(world);
	return profile;
}

void GMPPhysicsWorld::set_worker_count(int value) {
	workerCount = value;
}

int GMPPhysicsWorld::get_worker_count() const {
	return workerCount;
}

void GMPPhysicsWorld::set_sub_steps(int value) {
	subSteps = value;
}

int GMPPhysicsWorld::get_sub_steps() const {
	return subSteps;
}

void GMPPhysicsWorld::_bind_methods() {
	ClassDB::bind_method(D_METHOD("set_gravity", "value"), &GMPPhysicsWorld::set_gravity);
	ClassDB::bind_method(D_METHOD("get_gravity"), &GMPPhysicsWorld::get_gravity);
	ClassDB::bind_method(D_METHOD("set_worker_count", "value"), &GMPPhysicsWorld::set_worker_count);
	ClassDB::bind_method(D_METHOD("get_worker_count"), &GMPPhysicsWorld::get_worker_count);
	ClassDB::bind_method(D_METHOD("set_sub_steps", "value"), &GMPPhysicsWorld::set_sub_steps);
	ClassDB::bind_method(D_METHOD("get_sub_steps"), &GMPPhysicsWorld::get_sub_steps);
	ADD_PROPERTY(PropertyInfo(Variant::VECTOR3, "gravity"), "set_gravity", "get_gravity");
	ADD_PROPERTY(PropertyInfo(Variant::INT, "worker_count"), "set_worker_count", "get_worker_count");
	ADD_PROPERTY(PropertyInfo(Variant::INT, "sub_steps"), "set_sub_steps", "get_sub_steps");
	ClassDB::bind_method(D_METHOD("set_item_layer", "value"), &GMPPhysicsWorld::set_item_layer);
	ClassDB::bind_method(D_METHOD("get_item_layer"), &GMPPhysicsWorld::get_item_layer);
	ADD_PROPERTY(PropertyInfo(Variant::INT, "item_layer", PROPERTY_HINT_LAYERS_3D_PHYSICS), "set_item_layer", "get_item_layer");
	ClassDB::bind_method(D_METHOD("set_watcher_tags", "value"), &GMPPhysicsWorld::set_watcher_tags);
	ClassDB::bind_method(D_METHOD("get_watcher_tags"), &GMPPhysicsWorld::get_watcher_tags);
	ADD_PROPERTY(PropertyInfo(Variant::INT, "watcher_tags", PROPERTY_HINT_FLAGS, TagHintString), "set_watcher_tags", "get_watcher_tags");
	ClassDB::bind_method(D_METHOD("get_profile"), &GMPPhysicsWorld::get_profile);
	ClassDB::bind_method(D_METHOD("step", "delta"), &GMPPhysicsWorld::step);
	ClassDB::bind_method(D_METHOD("get_stats"), &GMPPhysicsWorld::get_stats);

	ClassDB::bind_method(D_METHOD("register_item_kind", "description"), &GMPPhysicsWorld::register_item_kind);
	ClassDB::bind_method(D_METHOD("spawn_item", "id", "kind", "authority", "transform", "linear_velocity", "angular_velocity"), &GMPPhysicsWorld::spawn_item);
	ClassDB::bind_method(D_METHOD("despawn_item", "id"), &GMPPhysicsWorld::despawn_item);
	ClassDB::bind_method(D_METHOD("clear_items"), &GMPPhysicsWorld::clear_items);
	ClassDB::bind_method(D_METHOD("get_item_count"), &GMPPhysicsWorld::get_item_count);
	ClassDB::bind_method(D_METHOD("get_items_below", "y"), &GMPPhysicsWorld::get_items_below);
	ClassDB::bind_method(D_METHOD("kick_random_items", "count"), &GMPPhysicsWorld::kick_random_items);

	ClassDB::bind_method(D_METHOD("has_body", "id"), &GMPPhysicsWorld::has_body);
	ClassDB::bind_method(D_METHOD("get_body_node", "id"), &GMPPhysicsWorld::get_body_node);
	ClassDB::bind_method(D_METHOD("get_body_transform", "id"), &GMPPhysicsWorld::get_body_transform);
	ClassDB::bind_method(D_METHOD("get_body_linear_velocity", "id"), &GMPPhysicsWorld::get_body_linear_velocity);
	ClassDB::bind_method(D_METHOD("get_body_angular_velocity", "id"), &GMPPhysicsWorld::get_body_angular_velocity);
	ClassDB::bind_method(D_METHOD("get_body_mass", "id"), &GMPPhysicsWorld::get_body_mass);
	ClassDB::bind_method(D_METHOD("is_body_asleep", "id"), &GMPPhysicsWorld::is_body_asleep);
	ClassDB::bind_method(D_METHOD("apply_body_force", "id", "force"), &GMPPhysicsWorld::apply_body_force);
	ClassDB::bind_method(D_METHOD("apply_body_impulse", "id", "impulse"), &GMPPhysicsWorld::apply_body_impulse);
	ClassDB::bind_method(D_METHOD("damp_body_spin", "id", "fraction"), &GMPPhysicsWorld::damp_body_spin);
	ClassDB::bind_method(D_METHOD("teleport_body", "id", "transform"), &GMPPhysicsWorld::teleport_body);
	ClassDB::bind_method(D_METHOD("set_body_tags", "id", "tags"), &GMPPhysicsWorld::set_body_tags);

	ClassDB::bind_method(D_METHOD("get_hits", "listener", "max_distance", "min_speed"), &GMPPhysicsWorld::get_hits);
	ClassDB::bind_method(D_METHOD("get_touches"), &GMPPhysicsWorld::get_touches);
	ClassDB::bind_method(D_METHOD("raycast", "from", "to", "mask"), &GMPPhysicsWorld::raycast, DEFVAL(0xFFFFFFFF));
	ClassDB::bind_method(D_METHOD("overlap_sphere", "center", "radius", "mask"), &GMPPhysicsWorld::overlap_sphere, DEFVAL(0xFFFFFFFF));
	ClassDB::bind_method(D_METHOD("overlap_box", "transform", "size", "mask"), &GMPPhysicsWorld::overlap_box, DEFVAL(0xFFFFFFFF));
	ClassDB::bind_method(D_METHOD("overlap_shape", "shape", "transform", "mask", "margin"), &GMPPhysicsWorld::overlap_shape, DEFVAL(0xFFFFFFFF), DEFVAL(0.0f));

	ClassDB::bind_method(D_METHOD("set_self_peer", "peer"), &GMPPhysicsWorld::set_self_peer);
	ClassDB::bind_method(D_METHOD("set_send_interval", "ticks"), &GMPPhysicsWorld::set_send_interval);
	ClassDB::bind_method(D_METHOD("set_body_authority", "id", "peer"), &GMPPhysicsWorld::set_body_authority);
	ClassDB::bind_method(D_METHOD("set_viewers", "positions", "eyes", "looks"), &GMPPhysicsWorld::set_viewers);
	ClassDB::bind_method(D_METHOD("hold_body", "id"), &GMPPhysicsWorld::hold_body);
	ClassDB::bind_method(D_METHOD("show_body_newest", "id"), &GMPPhysicsWorld::show_body_newest);
	ClassDB::bind_method(D_METHOD("set_playback_tick", "peer", "playback_tick"), &GMPPhysicsWorld::set_playback_tick);
	ClassDB::bind_method(D_METHOD("build_body_states", "max_bytes", "max_packet_bytes", "turn", "send_tick", "send_time"), &GMPPhysicsWorld::build_body_states);
	ClassDB::bind_method(D_METHOD("apply_body_states", "packet", "sender"), &GMPPhysicsWorld::apply_body_states);
}
