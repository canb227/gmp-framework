// GMPPhysicsWorld: what happened in a step. See physics_world.h.
//
// Box3D collects its events during the step and lists them afterwards. Moves are applied straight away.
// Sensor events become signals, which run game code that may despawn things, so they are first copied into a
// list that names the sensors by instance id, and only then sent: a sensor freed by an earlier signal is then
// simply skipped. Hits are a list C# reads after the step.

#include "physics_world.h"

#include "math_convert.h"
#include "physics_body.h"
#include "physics_character.h"
#include "physics_shape.h"
#include "tags.h"

#include <godot_cpp/core/object.hpp>

#include <cstring>

using namespace godot;

// Box3D lists every body that moved, with its new transform. Bodies at rest aren't listed, so a settled
// pile costs nothing here.
void GMPPhysicsWorld::read_move_events() {
	b3BodyEvents events = b3World_GetBodyEvents(world);
	for (int i = 0; i < events.moveCount; i++) {
		const b3BodyMoveEvent &event = events.moveEvents[i];
		int slot = (int)(uintptr_t)event.userData - 1;
		if (slot < 0 || slot >= (int)bodies.size() || !b3Body_IsValid(bodies[slot].body)) {
			continue;
		}
		Body &body = bodies[slot];
		body.previous = body.current;
		body.current = to_godot(event.transform);
		body.position = to_godot(event.transform.p);
		body.rotation = to_godot(event.transform.q);
		body.movedTick = tick;
		// Its peers should soon get the resting pose, not the last one sent while it settled.
		if (event.fellAsleep && !body.asleep && body.authority == selfPeer) {
			body.priorityAccumulator += RestingPoseBump;
		}
		body.asleep = event.fellAsleep;
		if (body.kind >= 0) {
			kinds[body.kind].changedTick = tick;
		}
		// A dynamic node moves with its body, simulated here or followed. (A kinematic body moves with its node.)
		if (body.node != nullptr && body.node->bodyType == GMPBody::DYNAMIC) {
			body.node->place_node(event.transform);
		}
	}
}

void GMPPhysicsWorld::read_sensor_events() {
	struct SensorEvent {
		ObjectID sensor;
		bool entered = false;
		uint64_t id = 0;
	};
	std::vector<SensorEvent> events;

	b3SensorEvents sensorEvents = b3World_GetSensorEvents(world);
	for (int i = 0; i < sensorEvents.beginCount; i++) {
		const b3SensorBeginTouchEvent &begin = sensorEvents.beginEvents[i];
		GMPShape *sensor = (GMPShape *)b3Shape_GetUserData(begin.sensorShapeId);
		Body *visitor = body_of_shape(begin.visitorShapeId);
		if (sensor != nullptr && visitor != nullptr && visitor->id != 0) {
			events.push_back(SensorEvent{ ObjectID(sensor->get_instance_id()), true, visitor->id });
		}
	}
	for (int i = 0; i < sensorEvents.endCount; i++) {
		const b3SensorEndTouchEvent &end = sensorEvents.endEvents[i];
		// The sensor or the visitor may be gone already (destroyed shapes end their overlaps too).
		if (!b3Shape_IsValid(end.sensorShapeId)) {
			continue;
		}
		GMPShape *sensor = (GMPShape *)b3Shape_GetUserData(end.sensorShapeId);
		Body *visitor = body_of_shape(end.visitorShapeId);
		if (sensor != nullptr && visitor != nullptr && visitor->id != 0) {
			events.push_back(SensorEvent{ ObjectID(sensor->get_instance_id()), false, visitor->id });
		}
	}

	for (const SensorEvent &event : events) {
		GMPShape *sensor = Object::cast_to<GMPShape>(ObjectDB::get_instance(event.sensor));
		if (sensor == nullptr) {
			continue;
		}
		if (event.entered) {
			sensor->emit_signal("entered", (int64_t)event.id);
		} else {
			sensor->emit_signal("exited", (int64_t)event.id);
		}
	}
}

void GMPPhysicsWorld::read_contact_events() {
	b3ContactEvents contactEvents = b3World_GetContactEvents(world);
	read_hits(contactEvents);
}

// Where one side of a hit ranks for being side A: lower comes first. body is the side's Body (nullptr if it has
// none), id its shared id (0 when other peers can't name it, so it has no tags) and tags its shape's tags.
static int hit_side_rank(const Body *body, uint64_t id, uint64_t tags) {
	// No id first: it has no tags, so nothing below would tell it apart from an untagged body.
	if (id == 0) {
		return 6;
	}
	if ((tags & PlayerTag) != 0) {
		return 0;
	}
	if ((tags & ToolTag) != 0) {
		return 1;
	}
	if (body != nullptr && body->kind >= 0) {
		return 2;
	}
	if ((tags & StructureTag) != 0) {
		return 3;
	}
	if ((tags & LevelTag) != 0) {
		return 4;
	}
	return 5;
}

// This tick's hits (get_hits) from Box3D's hit events: impacts at MinHitSpeed or more (the world's hit event
// threshold). Box3D only sees impacts of what this peer simulates: followers are kinematic and don't collide
// with each other or the level, so their hits come from their authority instead (play_received_hits).
void GMPPhysicsWorld::read_hits(const b3ContactEvents &contactEvents) {
	hits.clear();
	for (const HitRecord &hit : characterHits) {
		report_hit(hit);
	}
	characterHits.clear();
	for (int i = 0; i < contactEvents.hitCount; i++) {
		const b3ContactHitEvent &event = contactEvents.hitEvents[i];
		Body *a = body_of_shape(event.shapeIdA);
		Body *b = body_of_shape(event.shapeIdB);
		// A hit on a body this peer follows is its authority's to report: it is heard from them.
		bool followedA = a != nullptr && a->authority != 0 && a->authority != selfPeer;
		bool followedB = b != nullptr && b->authority != 0 && b->authority != selfPeer;
		if (followedA || followedB) {
			continue;
		}
		HitRecord hit = {};
		hit.position[0] = (float)event.point.x;
		hit.position[1] = (float)event.point.y;
		hit.position[2] = (float)event.point.z;
		hit.normal[0] = event.normal.x;
		hit.normal[1] = event.normal.y;
		hit.normal[2] = event.normal.z;
		hit.speed = event.approachSpeed;
		hit.shapeA = NoShapeIndex;
		hit.shapeB = NoShapeIndex;
		// Only sides other peers can name have tags, so a hit reads the same everywhere.
		if (a != nullptr && a->id != 0) {
			hit.idA = a->id;
			hit.shapeA = shape_index(event.shapeIdA);
			hit.tagsA = event.userMaterialIdA;
		}
		if (b != nullptr && b->id != 0) {
			hit.idB = b->id;
			hit.shapeB = shape_index(event.shapeIdB);
			hit.tagsB = event.userMaterialIdB;
		}
		// Side A is whichever side ranks first: player, tool, item, structure, level.
		if (hit_side_rank(b, hit.idB, hit.tagsB) < hit_side_rank(a, hit.idA, hit.tagsA)) {
			std::swap(hit.idA, hit.idB);
			std::swap(hit.shapeA, hit.shapeB);
			std::swap(hit.tagsA, hit.tagsB);
			hit.normal[0] = -hit.normal[0];
			hit.normal[1] = -hit.normal[1];
			hit.normal[2] = -hit.normal[2];
		}
		report_hit(hit);
	}
	play_received_hits();
}

void GMPPhysicsWorld::report_hit(const HitRecord &hit) {
	hits.push_back(hit);
	if ((int)hitsToSend.size() >= MaxHitsToSend) {
		return;
	}
	Vector3 point = Vector3(hit.position[0], hit.position[1], hit.position[2]);
	for (const Vector3 &viewer : viewerPositions) {
		if (point.distance_squared_to(viewer) <= HitRange * HitRange) {
			NetworkHit toSend;
			toSend.hit = hit;
			toSend.tick = (double)tick;
			toSend.sender = selfPeer;
			hitsToSend.push_back(toSend);
			return;
		}
	}
}

void GMPPhysicsWorld::add_character_hit(const HitRecord &hit) {
	characterHits.push_back(hit);
}

uint64_t GMPPhysicsWorld::named_shape_tags(uint64_t id, int32_t shapeIndex) {
	Body *body = find_body((int64_t)id);
	if (body == nullptr) {
		return 0;
	}
	if (body->kind >= 0) {
		const ItemKind &kind = kinds[body->kind];
		if (shapeIndex < 0 || shapeIndex >= (int)kind.shapes.size()) {
			return 0;
		}
		return kind.shapes[shapeIndex].tags | body->tags;
	}
	if (body->node == nullptr) {
		return 0;
	}
	// A shape that is no GMPShape (a character's capsule) has only its body's tags.
	if (shapeIndex == NoShapeIndex) {
		return body->node->tags;
	}
	std::vector<b3ShapeId> shapes(b3Body_GetShapeCount(body->body));
	int count = b3Body_GetShapes(body->body, shapes.data(), (int)shapes.size());
	for (int i = 0; i < count; i++) {
		GMPShape *shape = (GMPShape *)b3Shape_GetUserData(shapes[i]);
		if (shape != nullptr && shape->bodyShapeIndex == shapeIndex) {
			return b3Shape_GetSurfaceMaterial(shapes[i]).userMaterialId;
		}
	}
	return 0;
}

// Hits other peers sent join this tick's once their sender's playback clock gets to them, so they come with
// the followers' motion. Players are shown at their newest state (not played back), so a hit with a player in
// it (side A) plays as soon as it arrives; so does one whose sender has no clock here yet.
void GMPPhysicsWorld::play_received_hits() {
	for (int i = (int)hitsReceived.size() - 1; i >= 0; i--) {
		const NetworkHit &received = hitsReceived[i];
		auto clock = playbackTicks.find(received.sender);
		bool waiting = clock != playbackTicks.end() && clock->second < received.tick;
		if (waiting && (received.hit.tagsA & PlayerTag) == 0) {
			continue;
		}
		hits.push_back(received.hit);
		hitsReceived[i] = hitsReceived.back();
		hitsReceived.pop_back();
	}
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
