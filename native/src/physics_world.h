#pragma once

#include <box3d/box3d.h>

#include <godot_cpp/classes/material.hpp>
#include <godot_cpp/classes/mesh.hpp>
#include <godot_cpp/classes/node3d.hpp>
#include <godot_cpp/variant/packed_float32_array.hpp>

#include "physics_shape.h"

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

// One collider of an item kind, placed where it sits in the item, with its own surface and tags.
struct ItemKindShape {
	// Owned: its hull is freed with the kinds. Box3D shares one copy of a hull between every shape made from it.
	PlacedShape placed;
	float density = 1000.0f;
	float friction = 0.6f;
	float restitution = 0.0f;
	float rollingResistance = 0.0f;
	uint64_t tags = 0;
};

// Everything loose items of one kind share: their colliders, tags and meshes. Registered once per item type from
// C# (register_item_kind), then items are spawned by kind index.
struct ItemKind {
	std::vector<ItemKindShape> shapes;
	// The tags the whole item starts with (its ItemInfo's), on top of each collider's own; 0 if it has none.
	uint64_t tags = 0;
	bool castShadows = false;
	// Sync priority before the awake, nearness and held bonuses; -1 or lower never sends.
	int basePriority = 1;

	std::vector<ItemPart> parts;

	// The items of this kind, by slot in GMPPhysicsWorld::bodies, in the order they are drawn.
	std::vector<int> slots;
	// The last physics tick an item of this kind moved, appeared or went: the renderer skips kinds that
	// have been still for a while.
	uint64_t changedTick = 0;
};

// One state received for a body this peer follows, by its authority's physics tick.
struct Snapshot {
	uint64_t tick = 0;
	godot::Vector3 position;
	godot::Quaternion rotation;
	godot::Vector3 velocity;
	godot::Vector3 angularVelocity;
};

// States a follower keeps at most (half a second at 30 sends a second).
const int MaxSnapshots = 16;

// Added to a body's sync accumulator as it falls asleep, so its resting pose goes out soon.
const int RestingPoseBump = 100;

// A shape that is no GMPShape (a character's capsule) has this number in its body.
const int32_t NoShapeIndex = -1;

// One impact of the last step, as get_hits hands it over: C# reads the bytes straight into its ContactHit
// struct (gmp/physics/PhysicsWorld.cs), so the two must keep the same fields in the same order. Each side is the
// body's id and the shape's number in it, the same on every peer; a side with id 0 has no tags. The sides come in a
// fixed order: side A is never further down this list than side B: a player (its own landings and wall bumps, and
// things hitting it), a tool, a loose item, a structure, the level, anything else, a side with no id
// (hit_side_rank).
struct HitRecord {
	uint64_t idA;
	uint64_t idB;
	// The tags of the shape on each side (see tags.h).
	uint64_t tagsA;
	uint64_t tagsB;
	int32_t shapeA;
	int32_t shapeB;
	float position[3];
	// From side A toward side B.
	float normal[3];
	// How fast the two sides were approaching (m/s).
	float speed;
};
static_assert(sizeof(HitRecord) == 72, "HitRecord must match ContactHit in C#");

// Impacts slower than this (m/s) make no hit at all (Box3D's hit event threshold, and characters' own impacts).
const float MinHitSpeed = 2.0f;
// Hits are only sent to the other peers if they are this close to any player (m); HitStream.Range in C# is the same.
const float HitRange = 40.0f;

// Hits wait at most this many ticks for a packet to take them, so a sound is never sent late.
const uint64_t MaxHitSendAge = 10;
// At most this many wait at once (a huge spill sends the first ones only).
const int MaxHitsToSend = 512;

// A hit on its way to the other peers, or come from one and waiting for playback to reach it.
struct NetworkHit {
	HitRecord hit;
	// When it happened: the world's tick (to send), or the sender's physics tick (received).
	double tick = 0;
	uint64_t sender = 0;
};

// One physics body: a loose item (plain data, drawn through its kind's multimeshes) or a GMPBody node's body.
// Every body has an id that is the same on every peer (an item's, or the one C# gives a spawned scene's bodies),
// and its Box3D body's userData is its slot here, plus one.
//
// A synced body (authority set: every item, and node bodies C# hands to set_body_authority) is simulated by its
// authority, which sends its state; every other peer follows it.
struct Body {
	// 0 marks a free slot for an item, and a node body that has no id yet.
	uint64_t id = 0;
	b3BodyId body = b3_nullBodyId;
	// A loose item's kind, or -1 for a node's body.
	int kind = -1;
	// A node's body: its node. nullptr for a loose item.
	GMPBody *node = nullptr;
	// An item's whole-body tags (set_body_tags), on top of each collider's own. A node keeps its own in GMPBody.tags.
	uint64_t tags = 0;
	// Where its body was after the last step, and after the step before.
	godot::Transform3D current;
	godot::Transform3D previous;
	// The physics tick it last moved on. Older than this tick: it stood still, so previous is stale.
	uint64_t movedTick = 0;
	// The same pose as current, exactly as Box3D has it.
	godot::Vector3 position;
	godot::Quaternion rotation;
	// An item: its position in its kind's slots list.
	int indexInKind = -1;
	// Asleep in this peer's simulation.
	bool asleep = false;

	// ---- networking (physics_world_sync.cpp) ----
	// The peer that simulates it and sends its state; 0 for a body that isn't synced. Everyone else follows it.
	uint64_t authority = 0;
	// Sync priority before the bonuses (an item's from its kind); -1 or lower never sends.
	int basePriority = 1;
	// Grows by the body's priority each send and goes back to 0 when it is sent: highest goes first.
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
	Snapshot snapshots[MaxSnapshots];
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
	// Ticks it is shown ahead of the playback clock: up to the whole delay while show_body_newest is called.
	double playbackLead = 0;
	// The tick (the world's) show_body_newest was last called on.
	uint64_t showNewestTick = 0;
};

// Our Box3D world: owns the b3World and the table of every body in it (loose items and node bodies alike),
// draws the items through multimeshes, and syncs the bodies between peers. C# adds one to the scene and calls
// step() every physics tick. There is one at a time (get_current).
//
// The class is split over several files by topic, like a C# partial class:
//   physics_world.cpp          creating the world, stepping it, binding methods for C#
//   physics_world_bodies.cpp   item kinds, spawning items, node bodies, one body by id
//   physics_world_events.cpp   what happened in a step: moves, sensors, hits
//   physics_world_queries.cpp  ray casts and overlap tests
//   physics_world_sync.cpp     networking: building, sending and following body states
//   physics_world_render.cpp   filling the item multimeshes each tick
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

	// ---- bodies (physics_world_bodies.cpp) ----
	int register_item_kind(godot::Dictionary description);
	// The authority simulates it; on every other peer it follows the authority's states.
	void spawn_item(int64_t id, int kind, int64_t authority, godot::Transform3D transform, godot::Vector3 linearVelocity, godot::Vector3 angularVelocity);
	void despawn_item(int64_t id);
	void clear_items();
	int get_item_count() const;
	// Items this peer simulates that are below height y (fallen out of the world).
	godot::PackedInt64Array get_items_below(float y) const;
	// Testing: kicks this many random items upwards, so a pile keeps moving.
	void kick_random_items(int count);

	// A GMPBody's body joins the table when it is made (returning its slot) and leaves when it goes.
	int add_node_body(GMPBody *node);
	void remove_node_body(int slot);
	void set_body_id(int slot, uint64_t id);
	// The body a Box3D shape belongs to, or nullptr.
	Body *body_of_shape(b3ShapeId shapeId);
	// A shape's number in its body: an item collider's index in its kind, or a GMPShape's place in its body.
	int32_t shape_index(b3ShapeId shapeId);

	// One body by id, for game code that works on a few at a time (magnet rods, machines, placement).
	bool has_body(int64_t id) const;
	godot::Object *get_body_node(int64_t id) const;
	godot::Transform3D get_body_transform(int64_t id) const;
	godot::Vector3 get_body_linear_velocity(int64_t id) const;
	godot::Vector3 get_body_angular_velocity(int64_t id) const;
	float get_body_mass(int64_t id) const;
	bool is_body_asleep(int64_t id) const;
	void apply_body_force(int64_t id, godot::Vector3 force);
	void apply_body_impulse(int64_t id, godot::Vector3 impulse);
	// Takes this fraction (0 to 1) of the body's spin away.
	void damp_body_spin(int64_t id, float fraction);
	// Puts the body somewhere else at once (keeping its velocity).
	void teleport_body(int64_t id, godot::Transform3D transform);
	// Gives a whole item these tags (replacing what it was given before; its colliders keep their own).
	void set_body_tags(int64_t id, int64_t tags);

	// ---- events (physics_world_events.cpp) ----
	// This tick's impacts, as HitRecords back to back: the ones of what this peer simulates (Box3D's hit
	// events), and the ones other peers sent, as playback reaches them (so they come with what this peer
	// shows). Only those at least minSpeed (m/s) and within maxDistance of listener.
	godot::PackedByteArray get_hits(godot::Vector3 listener, float maxDistance, float minSpeed) const;
	// A character's impact from its move, for the next step's hits (C# moves the local player before the step,
	// so that is this tick's).
	void add_character_hit(const HitRecord &hit);

	// ---- queries (physics_world_queries.cpp): body ids ----
	// Sensors are never hit. mask: which collision layers count.
	godot::Dictionary raycast(godot::Vector3 from, godot::Vector3 to, int64_t mask);
	godot::PackedInt64Array overlap_sphere(godot::Vector3 center, float radius, int64_t mask);
	godot::PackedInt64Array overlap_box(godot::Transform3D transform, godot::Vector3 size, int64_t mask);
	// Everything the GMPShape would overlap, were it at transform (the shape's own world transform, scale
	// included), whether or not it is in the world, made margin metres smaller on every side (so a shape
	// resting flush on a floor doesn't count as in it). For placement previews.
	godot::PackedInt64Array overlap_shape(godot::Object *shape, godot::Transform3D transform, int64_t mask, float margin);

	// ---- networking (physics_world_sync.cpp) ----
	// This peer's id (Lobby.selfPeerID).
	void set_self_peer(int64_t peer);
	// Physics ticks between a body's sends (GameWorld.SendInterval). Followers play back two sends behind.
	void set_send_interval(int ticks);
	// Makes the body synced, simulated by peer and followed everywhere else.
	void set_body_authority(int64_t id, int64_t peer);
	// Every player's position and view (eye and look direction), each send, for the bodies' priorities.
	void set_viewers(godot::PackedVector3Array positions, godot::PackedVector3Array eyes, godot::PackedVector3Array looks);
	// A magnet rod holds the body this tick: it is sent often while held and for a second after.
	void hold_body(int64_t id);
	// Show this follower at its authority's newest state this tick, instead of two sends behind (what the
	// local magnet rod holds).
	void show_body_newest(int64_t id);
	// Which tick of peer's states followers show now (GameWorld.PlaybackTick, about two sends behind).
	void set_playback_tick(int64_t peer, double playbackTick);
	// The body states to send this tick: those whose turn it is (id % send interval == turn), highest
	// priority first, as packets of at most maxPacketBytes, up to maxBytes in all.
	godot::Array build_body_states(int maxBytes, int maxPacketBytes, int turn, int64_t sendTick, int64_t sendTime);
	// A packet from build_body_states on another peer.
	void apply_body_states(godot::PackedByteArray packet, int64_t sender);

protected:
	static void _bind_methods();
	void _notification(int what);

private:
	b3WorldId world = b3_nullWorldId;
	uint64_t tick = 0;

	std::vector<ItemKind> kinds;

	std::vector<Body> bodies;
	// Slots in bodies that are free to reuse.
	std::vector<int> freeSlots;
	std::unordered_map<uint64_t, int> slotById;
	int itemCount = 0;

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

	// This tick's hits (see get_hits).
	std::vector<HitRecord> hits;
	// Characters' impacts since the last step, for its hits.
	std::vector<HitRecord> characterHits;
	// Hits this peer reports, for the next packet; and hits received, until playback gets there.
	std::vector<NetworkHit> hitsToSend;
	std::vector<NetworkHit> hitsReceived;
	int64_t hitsSent = 0;
	int64_t hitsHeard = 0;

	// Timings of the last step, for get_stats.
	double stepMs = 0;
	double eventsMs = 0;
	double renderMs = 0;

	void create_world();
	void destroy_world();
	void free_kinds();

	int new_slot();
	void remove_item(int slot);
	void remove_from_followers(int slot);
	Body *find_body(int64_t id);
	// Puts an item's tags on each of its colliders.
	void apply_item_tags(Body &item);
	// The tags of the shape a received hit names, as this peer has it; 0 if it isn't here.
	uint64_t named_shape_tags(uint64_t id, int32_t shapeIndex);

	void read_move_events();
	void read_sensor_events();
	void read_contact_events();
	void read_hits(const b3ContactEvents &contactEvents);
	// Keeps a hit this peer reports for this tick, and queues it for the other peers if it is near a player.
	void report_hit(const HitRecord &hit);
	void play_received_hits();
	int write_hits(std::vector<uint8_t> &packet, int maxPacketBytes);
	int read_hits_from_packet(const uint8_t *packet, int size, uint64_t senderTick, int64_t sender);

	int body_priority(Body &body, uint64_t sendTick);
	bool at_rest_and_sent(const Body &body, uint64_t sendTick) const;
	int write_body_state(Body &body, uint64_t sendTick, uint8_t *to);
	void start_following(int slot);
	void play_followers(float delta);
	bool follow(Body &body, float delta);
	bool play_back(Body &body, double playbackTick, godot::Vector3 &position, godot::Quaternion &rotation);
	// Puts a body's pose where it is drawn from (and its node, if it has one), outside a step.
	void set_pose(Body &body, godot::Vector3 position, godot::Quaternion rotation);

	void update_rendering();
	void update_part_rendering(ItemKind &kind, ItemPart &part);
	void free_rendering();
};
