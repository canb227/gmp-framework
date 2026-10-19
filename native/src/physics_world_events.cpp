// GMPPhysicsWorld: what happened in a step. See physics_world.h.
//
// Box3D collects its events during the step and lists them afterwards. Moves are applied straight away.
// Sensor and touch events become signals, which run game code that may despawn things, so each kind is
// first copied into a list that names its nodes by instance id, and only then sent: a node freed by an
// earlier signal is then simply skipped.

#include "physics_world.h"

#include "body_user_data.h"
#include "math_convert.h"
#include "physics_body.h"
#include "physics_shape.h"

#include <godot_cpp/core/object.hpp>

#include <cstring>

using namespace godot;

// Who is on one side of an event: an item (itemId) or a body node (node), or neither.
struct EventSide {
	uint64_t itemId = 0;
	ObjectID node;
};

static EventSide event_side(const GMPPhysicsWorld *world, b3ShapeId shapeId) {
	EventSide side;
	uint64_t itemId = 0;
	GMPBody *node = nullptr;
	world->owner_of_shape(shapeId, itemId, node);
	side.itemId = itemId;
	if (node != nullptr) {
		side.node = ObjectID(node->get_instance_id());
	}
	return side;
}

void GMPPhysicsWorld::owner_of_shape(b3ShapeId shapeId, uint64_t &itemId, GMPBody *&node) const {
	itemId = 0;
	node = nullptr;
	if (!b3Shape_IsValid(shapeId)) {
		return;
	}
	void *userData = b3Body_GetUserData(b3Shape_GetBody(shapeId));
	if (user_data_is_item(userData)) {
		itemId = item_id_at_slot(item_slot_from_user_data(userData));
	} else {
		node = node_from_user_data(userData);
	}
}

// Box3D lists every body that moved, with its new transform. Bodies at rest aren't listed, so a settled
// pile costs nothing here.
void GMPPhysicsWorld::read_move_events() {
	b3BodyEvents events = b3World_GetBodyEvents(world);
	for (int i = 0; i < events.moveCount; i++) {
		const b3BodyMoveEvent &event = events.moveEvents[i];
		if (user_data_is_item(event.userData)) {
			int slot = item_slot_from_user_data(event.userData);
			if (slot >= (int)items.size() || items[slot].id == 0) {
				continue;
			}
			Item &item = items[slot];
			item.previous = item.current;
			item.current = to_godot(event.transform);
			item.position = to_godot(event.transform.p);
			item.rotation = to_godot(event.transform.q);
			item.movedTick = tick;
			// Its peers should soon get the resting pose, not the last one sent while it settled.
			if (event.fellAsleep && !item.asleep && item.authority == selfPeer) {
				item.priorityAccumulator += RestingPoseBump;
			}
			item.asleep = event.fellAsleep;
			kinds[item.kind].changedTick = tick;
			continue;
		}
		// A dynamic node moves with its body. (A kinematic body moves with its node, the other way round.)
		GMPBody *node = node_from_user_data(event.userData);
		if (node != nullptr && node->bodyType == GMPBody::DYNAMIC) {
			node->place_node(event.transform);
		}
	}
}

void GMPPhysicsWorld::read_sensor_events() {
	struct SensorEvent {
		ObjectID sensor;
		bool entered = false;
		EventSide visitor;
	};
	std::vector<SensorEvent> events;

	b3SensorEvents sensorEvents = b3World_GetSensorEvents(world);
	for (int i = 0; i < sensorEvents.beginCount; i++) {
		const b3SensorBeginTouchEvent &begin = sensorEvents.beginEvents[i];
		GMPShape *sensor = (GMPShape *)b3Shape_GetUserData(begin.sensorShapeId);
		if (sensor == nullptr) {
			continue;
		}
		SensorEvent event;
		event.sensor = ObjectID(sensor->get_instance_id());
		event.entered = true;
		event.visitor = event_side(this, begin.visitorShapeId);
		events.push_back(event);
	}
	for (int i = 0; i < sensorEvents.endCount; i++) {
		const b3SensorEndTouchEvent &end = sensorEvents.endEvents[i];
		// The sensor or the visitor may be gone already (destroyed shapes end their overlaps too).
		if (!b3Shape_IsValid(end.sensorShapeId)) {
			continue;
		}
		GMPShape *sensor = (GMPShape *)b3Shape_GetUserData(end.sensorShapeId);
		if (sensor == nullptr) {
			continue;
		}
		SensorEvent event;
		event.sensor = ObjectID(sensor->get_instance_id());
		event.entered = false;
		event.visitor = event_side(this, end.visitorShapeId);
		events.push_back(event);
	}

	for (const SensorEvent &event : events) {
		GMPShape *sensor = Object::cast_to<GMPShape>(ObjectDB::get_instance(event.sensor));
		if (sensor == nullptr) {
			continue;
		}
		if (event.visitor.itemId != 0) {
			if (event.entered) {
				sensor->emit_signal("item_entered", (int64_t)event.visitor.itemId);
			} else {
				sensor->emit_signal("item_exited", (int64_t)event.visitor.itemId);
			}
			continue;
		}
		Object *visitor = ObjectDB::get_instance(event.visitor.node);
		if (visitor == nullptr) {
			continue;
		}
		if (event.entered) {
			sensor->emit_signal("body_entered", visitor);
		} else {
			sensor->emit_signal("body_exited", visitor);
		}
	}
}

void GMPPhysicsWorld::read_contact_events() {
	b3ContactEvents contactEvents = b3World_GetContactEvents(world);
	read_hits(contactEvents);

	// Touches: only shapes with contact events on report them (items that report touches, bodies with
	// contact_monitor on). Each side that is a body node hears about the other side.
	struct Touch {
		ObjectID body;
		EventSide other;
	};
	std::vector<Touch> touches;
	itemTouches.clear();
	for (int i = 0; i < contactEvents.beginCount; i++) {
		const b3ContactBeginTouchEvent &begin = contactEvents.beginEvents[i];
		EventSide a = event_side(this, begin.shapeIdA);
		EventSide b = event_side(this, begin.shapeIdB);
		// Item on item: listed for C#'s tag reactions (get_item_touches).
		if (a.itemId != 0 && b.itemId != 0) {
			itemTouches.push_back((int64_t)a.itemId);
			itemTouches.push_back((int64_t)b.itemId);
			continue;
		}
		if (a.node.is_valid()) {
			touches.push_back(Touch{ a.node, b });
		}
		if (b.node.is_valid()) {
			touches.push_back(Touch{ b.node, a });
		}
	}

	for (const Touch &touch : touches) {
		GMPBody *body = Object::cast_to<GMPBody>(ObjectDB::get_instance(touch.body));
		if (body == nullptr || !body->contactMonitor) {
			continue;
		}
		if (touch.other.itemId != 0) {
			body->emit_signal("item_touched", (int64_t)touch.other.itemId);
			continue;
		}
		Object *other = ObjectDB::get_instance(touch.other.node);
		if (other != nullptr) {
			body->emit_signal("body_touched", other);
		}
	}
}

// Hits of items this peer simulates are sent to the other peers if they are this close to any player (m).
const float HitSendRange = 60.0f;

// This tick's hits (get_hits) from Box3D's hit events: impacts at 1 m/s or more (the world's hit event
// threshold) on shapes with hit events on (every item, and bodies with hit_events on). Box3D only sees
// impacts of what this peer simulates: followers are kinematic and don't collide with each other or the
// level, so their hits come from their authority instead (play_received_hits).
void GMPPhysicsWorld::read_hits(const b3ContactEvents &contactEvents) {
	hits.clear();
	for (int i = 0; i < contactEvents.hitCount; i++) {
		const b3ContactHitEvent &event = contactEvents.hitEvents[i];
		EventSide a = event_side(this, event.shapeIdA);
		EventSide b = event_side(this, event.shapeIdB);
		// With an item in it, the hit is its authority's to report: a body this peer simulates bumping into
		// an item someone else simulates is heard from them.
		bool hasItem = a.itemId != 0 || b.itemId != 0;
		bool ours = (a.itemId != 0 && items[slotById[a.itemId]].authority == selfPeer) ||
				(b.itemId != 0 && items[slotById[b.itemId]].authority == selfPeer);
		if (hasItem && !ours) {
			continue;
		}
		HitRecord hit;
		hit.position[0] = (float)event.point.x;
		hit.position[1] = (float)event.point.y;
		hit.position[2] = (float)event.point.z;
		hit.speed = event.approachSpeed;
		hit.surfaceTypeA = (int64_t)event.userMaterialIdA;
		hit.surfaceTypeB = (int64_t)event.userMaterialIdB;
		hit.itemIdA = a.itemId;
		hit.itemIdB = b.itemId;
		hit.nodeIdA = (uint64_t)a.node;
		hit.nodeIdB = (uint64_t)b.node;
		hits.push_back(hit);

		if (!hasItem || (int)hitsToSend.size() >= MaxHitsToSend) {
			continue;
		}
		Vector3 point = Vector3(hit.position[0], hit.position[1], hit.position[2]);
		bool nearAPlayer = false;
		for (const Vector3 &viewer : viewerPositions) {
			if (point.distance_squared_to(viewer) <= HitSendRange * HitSendRange) {
				nearAPlayer = true;
				break;
			}
		}
		if (nearAPlayer) {
			NetworkHit toSend;
			toSend.hit = hit;
			toSend.tick = (double)tick;
			toSend.sender = selfPeer;
			hitsToSend.push_back(toSend);
		}
	}
	play_received_hits();
}

// Hits other peers sent join this tick's once their sender's playback clock gets to them, so they come with
// the followers' motion. A hit whose sender has no clock here yet plays at once.
void GMPPhysicsWorld::play_received_hits() {
	for (int i = (int)hitsReceived.size() - 1; i >= 0; i--) {
		const NetworkHit &received = hitsReceived[i];
		auto clock = playbackTicks.find(received.sender);
		if (clock != playbackTicks.end() && clock->second < received.tick) {
			continue;
		}
		hits.push_back(received.hit);
		hitsReceived[i] = hitsReceived.back();
		hitsReceived.pop_back();
	}
}

PackedInt64Array GMPPhysicsWorld::get_item_touches() const {
	return itemTouches;
}

PackedByteArray GMPPhysicsWorld::get_hits(Vector3 listener, float maxDistance, float minSpeed) const {
	std::vector<HitRecord> wanted;
	for (const HitRecord &hit : hits) {
		Vector3 point = Vector3(hit.position[0], hit.position[1], hit.position[2]);
		if (hit.speed >= minSpeed && point.distance_squared_to(listener) <= maxDistance * maxDistance) {
			wanted.push_back(hit);
		}
	}
	PackedByteArray bytes;
	bytes.resize((int64_t)(wanted.size() * sizeof(HitRecord)));
	if (!wanted.empty()) {
		std::memcpy(bytes.ptrw(), wanted.data(), wanted.size() * sizeof(HitRecord));
	}
	return bytes;
}
