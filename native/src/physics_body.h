#pragma once

#include <box3d/box3d.h>

#include <godot_cpp/classes/node3d.hpp>

#include <vector>

class GMPShape;

// A physics body that is a node: for structures, level geometry, players and tools (loose items are not
// nodes, see GMPPhysicsWorld). Its colliders are the GMPShape nodes under it.
//
//   static     never moves (level geometry, placed structures). Move it with teleport().
//   kinematic  moves where its node is put: every tick the body is driven to the node's transform, with
//              the velocity that takes it there, so it pushes what it touches (moving parts, followers).
//   dynamic    simulated: the node is moved to wherever the body is after each step.
//
// The body is made when the node is ready (so its shapes are there) and goes when it leaves the tree.
class GMPBody : public godot::Node3D {
	GDCLASS(GMPBody, godot::Node3D)

public:
	enum BodyType {
		STATIC = 0,
		KINEMATIC = 1,
		DYNAMIC = 2,
	};

	BodyType bodyType = DYNAMIC;
	float gravityScale = 1.0f;
	float linearDamping = 0.0f;
	float angularDamping = 0.05f;
	bool canSleep = true;
	// Which layers the body is on, and which layers it collides with (bit masks, like Godot's).
	uint32_t collisionLayer = 1;
	uint32_t collisionMask = 0xFFFFFFFF;
	// Sends body_touched / item_touched when something starts touching it. Costs a little per contact.
	bool contactMonitor = false;
	// Adds its impacts to the world's hit list (impact sounds). Items always report theirs.
	bool hitEvents = false;
	// Off: the node has no body in the world at all (placement previews and the like).
	bool enabled = true;

	GMPBody();
	~GMPBody();

	b3BodyId get_body_id() const;
	bool has_body() const;
	// Called by GMPPhysicsWorld when its world goes: everything in it is gone already.
	void forget_body();

	// ---- moving it ----
	void teleport(godot::Transform3D transform);
	godot::Vector3 get_linear_velocity() const;
	void set_linear_velocity(godot::Vector3 velocity);
	godot::Vector3 get_angular_velocity() const;
	void set_angular_velocity(godot::Vector3 velocity);
	void apply_central_impulse(godot::Vector3 impulse);
	void apply_impulse(godot::Vector3 impulse, godot::Vector3 point);
	void apply_central_force(godot::Vector3 force);
	void apply_angular_impulse(godot::Vector3 impulse);
	float get_mass() const;
	bool is_awake() const;
	void set_awake(bool awake);

	// ---- called by GMPPhysicsWorld each tick ----
	// Kinematic: drive the body to where the node is now.
	void follow_node(float delta);
	// Dynamic: put the node where the body is.
	void place_node(const b3WorldTransform &transform);

	void set_body_type(int value);
	int get_body_type() const;
	void set_gravity_scale(float value);
	float get_gravity_scale() const;
	void set_linear_damping(float value);
	float get_linear_damping() const;
	void set_angular_damping(float value);
	float get_angular_damping() const;
	void set_can_sleep(bool value);
	bool get_can_sleep() const;
	void set_collision_layer(int64_t value);
	int64_t get_collision_layer() const;
	void set_collision_mask(int64_t value);
	int64_t get_collision_mask() const;
	void set_contact_monitor(bool value);
	bool get_contact_monitor() const;
	void set_hit_events(bool value);
	bool get_hit_events() const;
	void set_enabled(bool value);
	bool get_enabled() const;

protected:
	static void _bind_methods();
	void _notification(int what);

private:
	b3BodyId bodyId = b3_nullBodyId;
	// The node's scale when the body was made: Box3D bodies have none, so it is put back on the node.
	godot::Vector3 nodeScale = godot::Vector3(1, 1, 1);
	// Kinematic: the transform the body was last driven to, and whether it moved on the last tick.
	godot::Transform3D lastTarget;
	bool wasMoving = false;

	void create_body();
	void destroy_body();
	// Every GMPShape under this node whose nearest body is this one.
	void collect_shapes(godot::Node *node, std::vector<GMPShape *> &shapes);
	// Remakes the shapes after a change that every shape uses (layers, contact or hit events).
	void rebuild_shapes();
};

VARIANT_ENUM_CAST(GMPBody::BodyType);
