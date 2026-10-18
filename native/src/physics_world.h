#pragma once

#include <box3d/box3d.h>

#include <godot_cpp/classes/material.hpp>
#include <godot_cpp/classes/mesh.hpp>
#include <godot_cpp/classes/node3d.hpp>
#include <godot_cpp/variant/packed_float32_array.hpp>

#include "item_state.h"

#include <cstdint>
#include <unordered_map>
#include <vector>

class GMPBody;

// One mesh of an item kind, drawn for every item of that kind through one multimesh.
struct ItemPart {
	godot::Ref<godot::Mesh> mesh;
	// Where the mesh sits relative to the item's body.
	godot::Transform3D offset;
	// Replaces every surface's material when set (MeshInstance3D.material_override).
	godot::Ref<godot::Material> materialOverride;
	// Per surface replacements (MeshInstance3D.surface_material_override/N); empty entries keep the mesh's own.
	std::vector<godot::Ref<godot::Material>> surfaceMaterials;

	// Rendering, created the first time items of this kind are drawn (see physics_world_render.cpp).
	// A multimesh can't take per-surface materials, so a part with surface overrides draws its own copy
	// of the mesh with them set on it.
	godot::RID meshCopy;
	godot::RID multimesh;
	godot::RID instance;
	// Instances the multimesh has room for, and how many it draws.
	int capacity = 0;
	int visibleCount = 0;
	// 12 floats per instance (a 3x4 transform): this tick's transforms and last tick's, for physics interpolation.
	godot::PackedFloat32Array currentBuffer;
	godot::PackedFloat32Array previousBuffer;
};

// The collider shapes an item kind can have. The numbers match GMPShape::ShapeType.
enum ItemShape {
	ITEM_SHAPE_BOX = 0,
	ITEM_SHAPE_SPHERE = 1,
	ITEM_SHAPE_CAPSULE = 2,
	ITEM_SHAPE_CYLINDER = 3,
	ITEM_SHAPE_HULL = 4,
};

// Everything items of one kind share: their collider, surface and meshes. Registered once per item type
// from C# (register_item_kind), then items are spawned by kind index.
struct ItemKind {
	ItemShape shape = ITEM_SHAPE_BOX;
	// Only the one matching shape is used. Box3D copies a hull into the world when a shape is made from
	// it, and every item with the same hull shares that one copy.
	b3BoxHull box;
	b3Sphere sphere;
	b3Capsule capsule;
	// Cylinder and Hull. Owned: freed with the kinds.
	b3HullData *hull = nullptr;

	float density = 1.0f;
	float friction = 0.6f;
	float restitution = 0.0f;
	float rollingResistance = 0.0f;
	// The surface type, for impact sounds (GMPShape.surface_type).
	uint64_t userMaterialId = 0;
	bool castShadows = false;
	// Sync priority before the awake, nearness and held bonuses; -1 or lower never sends.
	int basePriority = 1;

	std::vector<ItemPart> parts;

	// The items of this kind, by slot in GMPPhysicsWorld::items, in the order they are drawn.
	std::vector<int> slots;
	// The last physics tick an item of this kind moved, appeared or went: the renderer skips kinds that
	// have been still for a while.
	uint64_t changedTick = 0;
};

// One state received for an item this peer follows, by its authority's physics tick.
struct ItemSnapshot {
	uint64_t tick = 0;
	godot::Vector3 position;
	godot::Quaternion rotation;
	godot::Vector3 velocity;
	godot::Vector3 angularVelocity;
};

// States a follower keeps at most (half a second at 30 sends a second).
const int MaxItemSnapshots = 16;

// Added to an item's sync accumulator as it falls asleep, so its resting pose goes out soon.
const int RestingPoseBump = 100;

// One loose item in the world. Items are plain data: no node per item.
struct Item {
	// The item's GMPObject id, which C# knows it by. 0 marks a free slot.
	uint64_t id = 0;
	int kind = -1;
	b3BodyId body = b3_nullBodyId;
	b3ShapeId shape = b3_nullShapeId;
	// Where its body was after the last step, and after the step before.
	godot::Transform3D current;
	godot::Transform3D previous;
	// The physics tick it last moved on. Older than this tick: it stood still, so previous is stale.
	uint64_t movedTick = 0;
	// The same pose as current, exactly as Box3D has it.
	godot::Vector3 position;
	godot::Quaternion rotation;
	// Its position in its kind's slots list.
	int indexInKind = -1;
	// Asleep in this peer's simulation (only means something on the authority).
	bool asleep = false;

	// ---- networking (physics_world_sync.cpp) ----
	// The peer that simulates it and sends its state. Everyone else follows it.
	uint64_t authority = 0;
	// Grows by the item's priority each send and goes back to 0 when it is sent: highest goes first.
	int priorityAccumulator = 0;
	// Until this tick (the world's) a magnet rod holds it, or let go of it a moment ago.
	uint64_t heldUntilTick = 0;
	// As of the last state built: the pose, whether it was at rest, and the send tick (C#'s physics frame).
	godot::Vector3 lastBuiltPosition;
	godot::Quaternion lastBuiltRotation;
	bool lastBuiltResting = false;
	// Resting sends still to go before it stops sending until it moves.
	int restingSendsLeft = 0;
	uint64_t lastBuiltSendTick = 0;

	// ---- following: on every peer but the authority ----
	// Send time of the newest state taken, to drop ones that arrive out of order.
	uint64_t lastStateTime = 0;
	// Received states, oldest first: the newest one at or before playback, then the ones after it.
	ItemSnapshot snapshots[MaxItemSnapshots];
	int snapshotCount = 0;
	bool following = false;
	// In the world's followers list (it can stop and start following again before the list drops it).
	bool inFollowers = false;
	// The velocities at the last playback, to carry on with should this peer become the authority.
	godot::Vector3 followVelocity;
	godot::Vector3 followAngularVelocity;
	// Where it is shown relative to the played-back pose: taken when following starts, then faded out, so
	// it slides onto the authority's pose instead of jumping.
	godot::Vector3 errorPosition;
	godot::Quaternion errorRotation;
	bool blendIn = false;
	bool blended = true;
	// Ticks it is shown ahead of the playback clock: up to the whole delay while show_item_newest is called.
	double playbackLead = 0;
	// The tick (the world's) show_item_newest was last called on.
	uint64_t showNewestTick = 0;
};

// Our Box3D world: owns the b3World and every loose item in it, draws the items through multimeshes,
// and is where GMPBody nodes make their bodies. C# adds one to the scene and calls step() every
// physics tick. There is one at a time (get_current).
//
// The class is split over several files by topic, like a C# partial class:
//   physics_world.cpp          creating the world, stepping it, binding methods for C#
//   physics_world_kinds.cpp    item kinds (colliders and meshes) from C# descriptions
//   physics_world_items.cpp    spawning, despawning and tracking items
//   physics_world_events.cpp   what happened in a step: moves, sensors, touches, hits
//   physics_world_queries.cpp  ray casts and overlap tests
//   physics_world_sync.cpp     networking: building, sending and following item states
//   physics_world_render.cpp   filling the multimeshes each tick
class GMPPhysicsWorld : public godot::Node3D {
	GDCLASS(GMPPhysicsWorld, godot::Node3D)

public:
	GMPPhysicsWorld();
	~GMPPhysicsWorld();

	// ---- settings, read when the world is created (on entering the tree) ----
	godot::Vector3 gravity = godot::Vector3(0, -9.81f, 0);
	int workerCount = 4;
	int subSteps = 4;
	// The collision layer every item is on, so queries can leave items out (or find only them).
	uint32_t itemLayer = 2;

	void set_gravity(godot::Vector3 value);
	godot::Vector3 get_gravity() const;
	void set_worker_count(int value);
	int get_worker_count() const;
	void set_sub_steps(int value);
	int get_sub_steps() const;
	void set_item_layer(int64_t value);
	int64_t get_item_layer() const;

	// ---- world (physics_world.cpp) ----
	// The world in the tree now, which GMPBody nodes make their bodies in. nullptr if there is none.
	static GMPPhysicsWorld *get_current();
	b3WorldId get_world_id() const;
	void step(float delta);
	godot::Dictionary get_stats() const;
	// Box3D's own timings of the last step (ms) and its counters.
	godot::Dictionary get_profile() const;

	// GMPBody nodes with a body in this world (they add and remove themselves).
	void add_node_body(GMPBody *body);
	void remove_node_body(GMPBody *body);

	// ---- events (physics_world_events.cpp) ----
	// The impacts of the last step: { points, speeds, surfaces, items } as packed arrays, one entry per
	// hit (surfaces and items: two per hit, the A and B side; an item id is 0 for a body that isn't one).
	godot::Dictionary get_hits() const;
	// The last step's new touches between items where one reports its touches (set_item_reports_touches):
	// item ids in pairs, A then B.
	godot::PackedInt64Array get_item_touches() const;

	// ---- queries (physics_world_queries.cpp) ----
	// Sensors are never hit. mask: which collision layers count.
	godot::Dictionary raycast(godot::Vector3 from, godot::Vector3 to, int64_t mask);
	godot::Dictionary overlap_sphere(godot::Vector3 center, float radius, int64_t mask);
	godot::Dictionary overlap_box(godot::Transform3D transform, godot::Vector3 size, int64_t mask);
	// Everything the GMPShape would overlap, were it at transform (the shape's own world transform, scale
	// included), whether or not it is in the world, made margin metres smaller on every side (so a shape
	// resting flush on a floor doesn't count as in it). For placement previews.
	godot::Dictionary overlap_shape(godot::Object *shape, godot::Transform3D transform, int64_t mask, float margin);

	// ---- item kinds (physics_world_kinds.cpp) ----
	int register_item_kind(godot::Dictionary description);

	// ---- items (physics_world_items.cpp) ----
	// The authority simulates it; on every other peer it follows the authority's states.
	void spawn_item(int64_t id, int kind, int64_t authority, godot::Transform3D transform, godot::Vector3 linearVelocity, godot::Vector3 angularVelocity);
	void despawn_item(int64_t id);
	void clear_items();
	int get_item_count() const;
	// The id of the item in slot, or 0 if the slot is free (for sensor and query results).
	uint64_t item_id_at_slot(int slot) const;
	// The item id (or 0) and node (or nullptr) a Box3D shape belongs to.
	void owner_of_shape(b3ShapeId shapeId, uint64_t &itemId, GMPBody *&node) const;
	// Testing: kicks this many random items upwards, so a pile keeps moving.
	void kick_random_items(int count);

	// One item, for game code that works on a few at a time (magnet rods, machines, placement).
	bool has_item(int64_t id) const;
	godot::Transform3D get_item_transform(int64_t id) const;
	godot::Vector3 get_item_linear_velocity(int64_t id) const;
	godot::Vector3 get_item_angular_velocity(int64_t id) const;
	float get_item_mass(int64_t id) const;
	bool is_item_asleep(int64_t id) const;
	void apply_item_force(int64_t id, godot::Vector3 force);
	void apply_item_impulse(int64_t id, godot::Vector3 impulse);
	// Takes this fraction (0 to 1) of the item's spin away.
	void damp_item_spin(int64_t id, float fraction);
	// Puts the item somewhere else at once (keeping its velocity).
	void teleport_item(int64_t id, godot::Transform3D transform);
	// Whether its new touches with other items are listed (get_item_touches), for tag reactions.
	void set_item_reports_touches(int64_t id, bool reports);
	// Items this peer simulates that are below height y (fallen out of the world).
	godot::PackedInt64Array get_items_below(float y) const;

	// ---- networking (physics_world_sync.cpp) ----
	// This peer's id (Lobby.selfPeerID).
	void set_self_peer(int64_t peer);
	// Physics ticks between an item's sends (GameWorld.SendInterval). Followers play back two sends behind.
	void set_send_interval(int ticks);
	void set_item_authority(int64_t id, int64_t peer);
	// Every player's position and view (eye and look direction), each send, for the items' priorities.
	void set_viewers(godot::PackedVector3Array positions, godot::PackedVector3Array eyes, godot::PackedVector3Array looks);
	// A magnet rod holds the item this tick: it is sent often while held and for a second after.
	void hold_item(int64_t id);
	// Show this follower at its authority's newest state this tick, instead of two sends behind (what the
	// local magnet rod holds).
	void show_item_newest(int64_t id);
	// Which tick of peer's states followers show now (GameWorld.PlaybackTick, about two sends behind).
	void set_playback_tick(int64_t peer, double playbackTick);
	// The item states to send this tick: those whose turn it is (id % send interval == turn), highest
	// priority first, as packets of at most maxPacketBytes, up to maxBytes in all.
	godot::Array build_item_states(int maxBytes, int maxPacketBytes, int turn, int64_t sendTick, int64_t sendTime);
	// A packet from build_item_states on another peer.
	void apply_item_states(godot::PackedByteArray packet, int64_t sender);

protected:
	static void _bind_methods();
	void _notification(int what);

private:
	b3WorldId world = b3_nullWorldId;
	uint64_t tick = 0;

	std::vector<ItemKind> kinds;

	std::vector<Item> items;
	// Slots in items that are free to reuse.
	std::vector<int> freeSlots;
	std::unordered_map<uint64_t, int> slotById;
	int itemCount = 0;

	std::vector<GMPBody *> nodeBodies;

	// ---- networking ----
	uint64_t selfPeer = 0;
	int sendInterval = 2;
	std::vector<int> followers;
	std::unordered_map<uint64_t, double> playbackTicks;
	std::vector<godot::Vector3> viewerPositions;
	std::vector<godot::Vector3> viewerEyes;
	std::vector<godot::Vector3> viewerLooks;
	// For get_stats: states built and applied, and the time it took, as running totals.
	int64_t statesBuilt = 0;
	int64_t statesApplied = 0;
	double buildMsTotal = 0;
	double applyMsTotal = 0;
	double followMsTotal = 0;

	// The last step's hits (see get_hits).
	godot::PackedVector3Array hitPoints;
	godot::PackedFloat32Array hitSpeeds;
	godot::PackedInt64Array hitSurfaces;
	godot::PackedInt64Array hitItems;
	godot::PackedInt64Array itemTouches;

	// Timings of the last step, for get_stats.
	double stepMs = 0;
	double eventsMs = 0;
	double renderMs = 0;

	void create_world();
	void destroy_world();
	void free_kinds();

	void remove_item(int slot);

	void read_move_events();
	void read_sensor_events();
	void read_contact_events();

	int item_priority(Item &item, uint64_t sendTick);
	bool at_rest_and_sent(const Item &item, uint64_t sendTick) const;
	ItemState build_item_state(Item &item, uint64_t sendTick);
	void start_following(int slot);
	void play_followers(float delta);
	bool follow(Item &item, float delta);
	bool play_back(Item &item, double playbackTick, godot::Vector3 &position, godot::Quaternion &rotation);

	void update_rendering();
	void update_part_rendering(ItemKind &kind, ItemPart &part);
	void free_rendering();
};
