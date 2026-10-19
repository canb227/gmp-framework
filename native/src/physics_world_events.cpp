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

	// Hits go in the list C# reads after the step (get_hits). A hit is a new contact that was approaching at
	// HitSpeed or more. Box3D's own hit events need the solver to have pushed back, which never happens for a
	// follower: it is aimed at a pose the authority already found touching, not overlapping. A new contact's
	// approach speed (before the solver) is there either way, so the authority and its followers hear the
	// same impacts.
	hitPoints.clear();
	hitSpeeds.clear();
	hitSurfaces.clear();
	hitItems.clear();

	// Touches: only bodies with contact_monitor on have their shapes report them. Each side that is a
	// body node hears about the other side.
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
		read_hit(begin, a.itemId, b.itemId);
		// Item on item: listed for C#'s tag reactions (get_item_touches), if either asked for it.
		if (a.itemId != 0 && b.itemId != 0) {
			if (items[slotById[a.itemId]].reportsTouches || items[slotById[b.itemId]].reportsTouches) {
				itemTouches.push_back((int64_t)a.itemId);
				itemTouches.push_back((int64_t)b.itemId);
			}
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

// A new contact approaching at least this fast (m/s) is a hit.
const float HitSpeed = 1.0f;

// How fast one side of a contact is moving along direction (m/s). A follower item: its authority's velocity
// in the state being played or the one just passed, whichever is faster that way. Playback may have reached
// the state from after the impact by the time the contact begins. Anything else: its body's velocity.
float GMPPhysicsWorld::side_speed(b3ShapeId shapeId, uint64_t itemId, Vector3 direction) const {
	if (itemId != 0) {
		const Item &item = items[slotById.at(itemId)];
		if (item.following && item.snapshotCount > 0) {
			return std::max(item.snapshots[0].velocity.dot(direction), item.passedVelocity.dot(direction));
		}
	}
	return to_godot(b3Body_GetLinearVelocity(b3Shape_GetBody(shapeId))).dot(direction);
}

// Adds a new contact to the hit list if it is an impact: an item on either side, or a body node with
// hit_events on, approaching at HitSpeed or more.
void GMPPhysicsWorld::read_hit(const b3ContactBeginTouchEvent &begin, uint64_t itemA, uint64_t itemB) {
	if (itemA == 0 && itemB == 0) {
		uint64_t unused = 0;
		GMPBody *nodeA = nullptr;
		GMPBody *nodeB = nullptr;
		owner_of_shape(begin.shapeIdA, unused, nodeA);
		owner_of_shape(begin.shapeIdB, unused, nodeB);
		bool wanted = (nodeA != nullptr && nodeA->hitEvents) || (nodeB != nullptr && nodeB->hitEvents);
		if (!wanted) {
			return;
		}
	}
	if (!b3Contact_IsValid(begin.contactId)) {
		return;
	}
	// The fastest approaching point. Box3D works out each point's normal velocity before solving, while
	// either shape has hit events on.
	b3ContactData data = b3Contact_GetData(begin.contactId);
	float speed = 0.0f;
	b3Vec3 anchorA = { 0, 0, 0 };
	for (int m = 0; m < data.manifoldCount; m++) {
		const b3Manifold &manifold = data.manifolds[m];
		for (int p = 0; p < manifold.pointCount; p++) {
			float approach = -manifold.points[p].normalVelocity;
			if (approach > speed) {
				speed = approach;
				anchorA = manifold.points[p].anchorA;
			}
		}
	}
	// A follower's body only goes as far as its next played-back pose, so as it lands it slows to cover the
	// last bit of the gap. How fast it really came in is the authority's velocity in the state being played
	// (from before the impact).
	bool followerA = itemA != 0 && items[slotById[itemA]].following;
	bool followerB = itemB != 0 && items[slotById[itemB]].following;
	if ((followerA || followerB) && data.manifoldCount > 0) {
		// The normal points from A to B: A moving along it and B against it close the gap.
		Vector3 normal = to_godot(data.manifolds[0].normal);
		float approach = side_speed(begin.shapeIdA, itemA, normal) + side_speed(begin.shapeIdB, itemB, -normal);
		if (approach > speed) {
			speed = approach;
			if (data.manifolds[0].pointCount > 0) {
				anchorA = data.manifolds[0].points[0].anchorA;
			}
		}
	}
	if (speed < HitSpeed) {
		return;
	}
	// The anchor is from body A's centre of mass.
	b3BodyId bodyA = b3Shape_GetBody(begin.shapeIdA);
	b3Pos centerA = b3Body_GetWorldPoint(bodyA, b3Body_GetLocalCenter(bodyA));
	hitPoints.push_back(Vector3(centerA.x + anchorA.x, centerA.y + anchorA.y, centerA.z + anchorA.z));
	hitSpeeds.push_back(speed);
	hitSurfaces.push_back((int64_t)b3Shape_GetSurfaceMaterial(begin.shapeIdA).userMaterialId);
	hitSurfaces.push_back((int64_t)b3Shape_GetSurfaceMaterial(begin.shapeIdB).userMaterialId);
	hitItems.push_back((int64_t)itemA);
	hitItems.push_back((int64_t)itemB);
}

PackedInt64Array GMPPhysicsWorld::get_item_touches() const {
	return itemTouches;
}

Dictionary GMPPhysicsWorld::get_hits() const {
	Dictionary hits;
	hits["points"] = hitPoints;
	hits["speeds"] = hitSpeeds;
	hits["surfaces"] = hitSurfaces;
	hits["items"] = hitItems;
	return hits;
}
