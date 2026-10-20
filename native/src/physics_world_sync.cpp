// GMPPhysicsWorld: networking the bodies. See physics_world.h.
//
// Each synced body (every loose item, and the node bodies C# hands to set_body_authority) has one authority,
// the peer that simulates it. The authority sends its state; every other peer follows: the body is kinematic
// there, and is driven along the authority's states, played back about two sends behind the newest so packets
// arriving unevenly don't show. C# moves the packets (GameWorld) and keeps the playback clock per sending peer
// (set_playback_tick).
//
// Sending: on each send, every body whose turn it is (id % send interval) adds its priority to its
// accumulator, and the states go out highest accumulator first until the budget for this send is used up.
// A sent body starts again from 0, so everything gets sent eventually, the important things more often.
// A body at rest stops sending once its resting pose has gone out a few times, until it moves.
//
// A packet:   send time (8 bytes, GameWorld.netTime)   sender's physics tick (8 bytes)
//             hit count (2 bytes), then the hits (HitWireSize each, see write_hits)
//             then per body: id (8 bytes), state size (1 byte), state (see write_state)
//
// Hits: the impacts the sender reported since its last packet, so every peer hears what its followers do
// (they don't collide here). Each is played back with the states it came with.

#include "physics_world.h"

#include "math_convert.h"
#include "physics_body.h"

#include <godot_cpp/classes/engine.hpp>

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstring>

using namespace godot;

// One body's state on the wire:
//
//   position          3 floats                                       12 bytes
//   rotation          the three smaller quaternion parts as shorts,   7 bytes
//                     then which part was left out (0-3)
//   velocity          3 half floats, only when moving                  6 bytes
//   angular velocity  3 half floats, only when moving                  6 bytes
//
// The largest quaternion part is left out: the other three give it back, as the quaternion has length 1,
// and with it made positive (the same rotation) it needs no sign. The others are at most 1/sqrt(2), so
// times StatePartScale they fit a short. A resting state has no velocities: 19 bytes, else 31.

struct BodyState {
	Vector3 position;
	Quaternion rotation;
	Vector3 velocity;
	Vector3 angularVelocity;
};

const int StateRestingSize = 19;
const int StateMovingSize = 31;
const float StatePartScale = 32767.0f * 1.41421356f;

static void write_float(uint8_t *to, float value) {
	std::memcpy(to, &value, 4);
}

static float read_float(const uint8_t *from) {
	float value;
	std::memcpy(&value, from, 4);
	return value;
}

static void write_short(uint8_t *to, int16_t value) {
	std::memcpy(to, &value, 2);
}

static int16_t read_short(const uint8_t *from) {
	int16_t value;
	std::memcpy(&value, from, 2);
	return value;
}

static void write_half(uint8_t *to, float value) {
	uint16_t half = Math::make_half_float(value);
	std::memcpy(to, &half, 2);
}

static float read_half(const uint8_t *from) {
	uint16_t half;
	std::memcpy(&half, from, 2);
	return Math::half_to_float(half);
}

static void write_u64(uint8_t *to, uint64_t value) {
	std::memcpy(to, &value, 8);
}

static uint64_t read_u64(const uint8_t *from) {
	uint64_t value;
	std::memcpy(&value, from, 8);
	return value;
}

// Writes the state and returns how many bytes it took (StateRestingSize or StateMovingSize).
static int write_state(uint8_t *to, const BodyState &state) {
	write_float(to + 0, state.position.x);
	write_float(to + 4, state.position.y);
	write_float(to + 8, state.position.z);

	Quaternion q = state.rotation.normalized();
	float parts[4] = { q.x, q.y, q.z, q.w };
	int largest = 0;
	for (int i = 1; i < 4; i++) {
		if (std::fabs(parts[i]) > std::fabs(parts[largest])) {
			largest = i;
		}
	}
	if (parts[largest] < 0) {
		for (int i = 0; i < 4; i++) {
			parts[i] = -parts[i];
		}
	}
	int written = 0;
	for (int i = 0; i < 4; i++) {
		if (i != largest) {
			float scaled = std::round(parts[i] * StatePartScale);
			if (scaled > 32767.0f) {
				scaled = 32767.0f;
			}
			if (scaled < -32767.0f) {
				scaled = -32767.0f;
			}
			write_short(to + 12 + 2 * written, (int16_t)scaled);
			written++;
		}
	}
	to[18] = (uint8_t)largest;

	bool resting = state.velocity == Vector3() && state.angularVelocity == Vector3();
	if (resting) {
		return StateRestingSize;
	}
	write_half(to + 19, state.velocity.x);
	write_half(to + 21, state.velocity.y);
	write_half(to + 23, state.velocity.z);
	write_half(to + 25, state.angularVelocity.x);
	write_half(to + 27, state.angularVelocity.y);
	write_half(to + 29, state.angularVelocity.z);
	return StateMovingSize;
}

// Reads a state of size bytes (StateRestingSize or StateMovingSize).
static BodyState read_body_state(const uint8_t *from, int size) {
	BodyState state;
	state.position = Vector3(read_float(from + 0), read_float(from + 4), read_float(from + 8));

	int largest = from[18] & 3;
	float parts[4] = { 0, 0, 0, 0 };
	float sumOfSquares = 0;
	int read = 0;
	for (int i = 0; i < 4; i++) {
		if (i != largest) {
			parts[i] = read_short(from + 12 + 2 * read) / StatePartScale;
			sumOfSquares += parts[i] * parts[i];
			read++;
		}
	}
	float rest = 1.0f - sumOfSquares;
	if (rest < 0) {
		rest = 0;
	}
	parts[largest] = std::sqrt(rest);
	state.rotation = Quaternion(parts[0], parts[1], parts[2], parts[3]);

	if (size >= StateMovingSize) {
		state.velocity = Vector3(read_half(from + 19), read_half(from + 21), read_half(from + 23));
		state.angularVelocity = Vector3(read_half(from + 25), read_half(from + 27), read_half(from + 29));
	}
	return state;
}

static double now_ms() {
	return std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now().time_since_epoch()).count();
}

static int ticks_per_second() {
	return Engine::get_singleton()->get_physics_ticks_per_second();
}

// ---- priority ----

// Up to this many times the priority right next to a player, falling evenly to 1x at ProximityRange (m).
const float ProximityMultiplier = 10.0f;
const float ProximityRange = 30.0f;
// Times the priority of a body inside any player's view cone (about 57 degrees either side of where they
// look), so what someone looks at is updated more often than what's behind them.
const float ViewMultiplier = 2.5f;
const float ViewConeCos = 0.55f;
// Priority of a body a magnet rod holds: above any nearness bonus, below the players.
const int HeldPriority = 100;
// Times a resting pose is sent before the body goes quiet (each is unreliable), and how often a quiet
// body still sends its pose, in case every copy of it was lost.
const int RestingSends = 3;
const int RestingHeartbeatSeconds = 10;

// ---- following ----

// How long a follower carries the newest state on at its velocity once playback has passed it.
const float MaxExtrapolationSeconds = 0.25f;
// Time constant (s) of the slide onto the authority's pose after following starts.
const float ErrorBlendSeconds = 0.1f;
// Within these the slide is done: metres, and 1 - |w| of the rotation between the poses (about 0.16 degrees).
const float ArrivedDistance = 0.0005f;
const float ArrivedRotation = 1e-6f;

void GMPPhysicsWorld::set_self_peer(int64_t peer) {
	selfPeer = (uint64_t)peer;
}

void GMPPhysicsWorld::set_send_interval(int ticks) {
	sendInterval = std::max(1, ticks);
}

void GMPPhysicsWorld::set_viewers(PackedVector3Array positions, PackedVector3Array eyes, PackedVector3Array looks) {
	viewerPositions.clear();
	viewerEyes.clear();
	viewerLooks.clear();
	for (int i = 0; i < positions.size(); i++) {
		viewerPositions.push_back(positions[i]);
	}
	for (int i = 0; i < eyes.size() && i < looks.size(); i++) {
		viewerEyes.push_back(eyes[i]);
		viewerLooks.push_back(looks[i].normalized());
	}
}

void GMPPhysicsWorld::hold_body(int64_t id) {
	auto found = slotById.find((uint64_t)id);
	if (found != slotById.end()) {
		bodies[found->second].heldUntilTick = tick + ticks_per_second();
	}
}

void GMPPhysicsWorld::show_body_newest(int64_t id) {
	auto found = slotById.find((uint64_t)id);
	if (found != slotById.end()) {
		bodies[found->second].showNewestTick = tick;
	}
}

void GMPPhysicsWorld::set_playback_tick(int64_t peer, double playbackTick) {
	playbackTicks[(uint64_t)peer] = playbackTick;
}

// A node body's own type comes back when this peer simulates it; a node body that isn't dynamic (a static
// structure, a kinematic part moved by its node) has nothing to follow and stays as it is.
void GMPPhysicsWorld::set_body_authority(int64_t id, int64_t peer) {
	auto found = slotById.find((uint64_t)id);
	if (found == slotById.end()) {
		return;
	}
	Body &body = bodies[found->second];
	if (body.node != nullptr && body.node->bodyType != GMPBody::DYNAMIC) {
		return;
	}
	body.authority = (uint64_t)peer;
	// The new authority's followers need its resting pose again, and its ticks are counted by its own clock.
	body.lastBuiltResting = false;
	body.snapshotCount = 0;
	body.lastStateTime = 0;
	body.following = false;
	if (body.authority == selfPeer) {
		// A follower is only ever driven along, so it carries on at the velocity it was followed at.
		b3Body_SetType(body.body, b3_dynamicBody);
		b3Body_SetLinearVelocity(body.body, to_b3(body.followVelocity));
		b3Body_SetAngularVelocity(body.body, to_b3(body.followAngularVelocity));
		b3Body_SetAwake(body.body, true);
		body.asleep = false;
	} else {
		b3Body_SetType(body.body, b3_kinematicBody);
		b3Body_SetLinearVelocity(body.body, b3Vec3{ 0, 0, 0 });
		b3Body_SetAngularVelocity(body.body, b3Vec3{ 0, 0, 0 });
	}
}

// True while the body is at rest, its resting pose has gone out RestingSends times and it hasn't moved
// since: a send would only repeat it.
bool GMPPhysicsWorld::at_rest_and_sent(const Body &body, uint64_t sendTick) const {
	if (!body.lastBuiltResting || body.restingSendsLeft > 0) {
		return false;
	}
	if (sendTick - body.lastBuiltSendTick >= (uint64_t)(RestingHeartbeatSeconds * ticks_per_second())) {
		return false;
	}
	return body.position == body.lastBuiltPosition && body.rotation == body.lastBuiltRotation;
}

int GMPPhysicsWorld::body_priority(Body &body, uint64_t sendTick) {
	int basePriority = body.basePriority;
	if (basePriority <= -1 || at_rest_and_sent(body, sendTick)) {
		return -1;
	}
	int unscaled = basePriority;
	if (!body.asleep) {
		unscaled += 1;
	}
	float nearestSquared = ProximityRange * ProximityRange;
	for (const Vector3 &viewer : viewerPositions) {
		float distanceSquared = body.position.distance_squared_to(viewer);
		if (distanceSquared < nearestSquared) {
			nearestSquared = distanceSquared;
		}
	}
	float nearest = std::sqrt(nearestSquared);
	float multiplier = 1.0f + (ProximityMultiplier - 1.0f) * (1.0f - nearest / ProximityRange);
	for (int i = 0; i < (int)viewerEyes.size(); i++) {
		Vector3 toBody = body.position - viewerEyes[i];
		float distance = toBody.length();
		if (distance > 0 && viewerLooks[i].dot(toBody) >= ViewConeCos * distance) {
			multiplier *= ViewMultiplier;
			break;
		}
	}
	int priority = (int)std::round(unscaled * multiplier);
	if (tick < body.heldUntilTick && priority < HeldPriority) {
		priority = HeldPriority;
	}
	return priority;
}

int GMPPhysicsWorld::write_body_state(Body &body, uint64_t sendTick, uint8_t *to) {
	BodyState state;
	state.position = body.position;
	state.rotation = body.rotation;
	// At rest it goes out without velocities, so followers don't drift on whatever it fell asleep with.
	bool resting = body.asleep;
	if (!resting) {
		state.velocity = to_godot(b3Body_GetLinearVelocity(body.body));
		state.angularVelocity = to_godot(b3Body_GetAngularVelocity(body.body));
	}
	bool unmoved = body.position == body.lastBuiltPosition && body.rotation == body.lastBuiltRotation;
	if (resting && body.lastBuiltResting && unmoved) {
		body.restingSendsLeft = std::max(0, body.restingSendsLeft - 1);
	} else if (resting) {
		body.restingSendsLeft = RestingSends - 1;
	}
	body.lastBuiltPosition = body.position;
	body.lastBuiltRotation = body.rotation;
	body.lastBuiltResting = resting;
	body.lastBuiltSendTick = sendTick;
	return write_state(to, state);
}

static PackedByteArray to_packet(const std::vector<uint8_t> &bytes) {
	PackedByteArray packet;
	packet.resize((int64_t)bytes.size());
	std::memcpy(packet.ptrw(), bytes.data(), bytes.size());
	return packet;
}

Array GMPPhysicsWorld::build_body_states(int maxBytes, int maxPacketBytes, int turn, int64_t sendTick, int64_t sendTime) {
	double start = now_ms();
	Array packets;

	std::vector<int> candidates;
	for (int slot = 0; slot < (int)bodies.size(); slot++) {
		Body &body = bodies[slot];
		if (body.id == 0 || body.authority != selfPeer || (int)(body.id % (uint64_t)sendInterval) != turn) {
			continue;
		}
		int priority = body_priority(body, (uint64_t)sendTick);
		if (priority <= -1) {
			continue;
		}
		body.priorityAccumulator += priority;
		candidates.push_back(slot);
	}
	// Highest accumulator first (std::sort takes the comparison as a function).
	std::sort(candidates.begin(), candidates.end(), [this](int a, int b) {
		return bodies[a].priorityAccumulator > bodies[b].priorityAccumulator;
	});

	// Every packet has the header and a hit count; only the first carries hits.
	const int headerSize = 18;
	std::vector<uint8_t> packet(headerSize, 0);
	write_u64(packet.data(), (uint64_t)sendTime);
	write_u64(packet.data() + 8, (uint64_t)sendTick);
	int hitsWritten = write_hits(packet, maxPacketBytes);
	int sentBytes = (int)packet.size() - headerSize;
	for (int slot : candidates) {
		Body &body = bodies[slot];
		uint8_t stateBytes[StateMovingSize];
		int stateSize = write_body_state(body, (uint64_t)sendTick, stateBytes);
		statesBuilt++;
		int entrySize = 8 + 1 + stateSize;
		if (sentBytes + entrySize > maxBytes) {
			// This send is full: the rest wait for the next one (without being built).
			break;
		}
		if ((int)packet.size() > headerSize && (int)packet.size() + entrySize > maxPacketBytes) {
			packets.push_back(to_packet(packet));
			packet.resize(headerSize);
			packet[16] = 0;
			packet[17] = 0;
		}
		size_t at = packet.size();
		packet.resize(at + entrySize);
		write_u64(packet.data() + at, body.id);
		packet[at + 8] = (uint8_t)stateSize;
		std::memcpy(packet.data() + at + 9, stateBytes, stateSize);
		sentBytes += entrySize;
		body.priorityAccumulator = 0;
	}
	if ((int)packet.size() > headerSize || (packets.size() == 0 && hitsWritten > 0)) {
		packets.push_back(to_packet(packet));
	}
	buildMsTotal += now_ms() - start;
	return packets;
}

// ---- hits on the wire ----

// position (3 floats), ticks since it happened (1 byte), speed (half float), then each side's id (8 bytes) and shape
// number (2 bytes). The receiver looks the tags up itself.
const int HitWireSize = 35;

// Writes the hit count and as many waiting hits as fit after the header; returns how many went.
int GMPPhysicsWorld::write_hits(std::vector<uint8_t> &packet, int maxPacketBytes) {
	// Too old to be worth hearing now.
	for (int i = (int)hitsToSend.size() - 1; i >= 0; i--) {
		if ((uint64_t)hitsToSend[i].tick + MaxHitSendAge < tick) {
			hitsToSend.erase(hitsToSend.begin() + i);
		}
	}
	int room = (maxPacketBytes - (int)packet.size()) / HitWireSize;
	int count = std::min({ (int)hitsToSend.size(), room, 65535 });
	if (count < 0) {
		count = 0;
	}
	packet[16] = (uint8_t)(count & 0xFF);
	packet[17] = (uint8_t)(count >> 8);
	for (int i = 0; i < count; i++) {
		const NetworkHit &toSend = hitsToSend[i];
		size_t at = packet.size();
		packet.resize(at + HitWireSize);
		uint8_t *to = packet.data() + at;
		write_float(to, toSend.hit.position[0]);
		write_float(to + 4, toSend.hit.position[1]);
		write_float(to + 8, toSend.hit.position[2]);
		to[12] = (uint8_t)std::min<uint64_t>(tick - (uint64_t)toSend.tick, 255);
		write_half(to + 13, toSend.hit.speed);
		write_u64(to + 15, toSend.hit.idA);
		write_short(to + 23, (int16_t)toSend.hit.shapeA);
		write_u64(to + 25, toSend.hit.idB);
		write_short(to + 33, (int16_t)toSend.hit.shapeB);
	}
	hitsToSend.erase(hitsToSend.begin(), hitsToSend.begin() + count);
	hitsSent += count;
	return count;
}

// Reads a packet's hits (after its 16 byte header) into hitsReceived; returns the bytes they took, or -1 if
// the packet is too short for them.
int GMPPhysicsWorld::read_hits_from_packet(const uint8_t *packet, int size, uint64_t senderTick, int64_t sender) {
	if (size < 18) {
		return -1;
	}
	int count = packet[16] | (packet[17] << 8);
	if (18 + count * HitWireSize > size) {
		return -1;
	}
	for (int i = 0; i < count; i++) {
		const uint8_t *from = packet + 18 + i * HitWireSize;
		NetworkHit received;
		received.hit.position[0] = read_float(from);
		received.hit.position[1] = read_float(from + 4);
		received.hit.position[2] = read_float(from + 8);
		received.tick = (double)senderTick - from[12];
		received.hit.speed = read_half(from + 13);
		received.hit.idA = read_u64(from + 15);
		received.hit.shapeA = read_short(from + 23);
		received.hit.idB = read_u64(from + 25);
		received.hit.shapeB = read_short(from + 33);
		received.hit.tagsA = named_shape_tags(received.hit.idA, received.hit.shapeA);
		received.hit.tagsB = named_shape_tags(received.hit.idB, received.hit.shapeB);
		received.sender = (uint64_t)sender;
		hitsReceived.push_back(received);
	}
	hitsHeard += count;
	return 2 + count * HitWireSize;
}

// ---- receiving ----

void GMPPhysicsWorld::apply_body_states(PackedByteArray packetArray, int64_t sender) {
	double start = now_ms();
	const uint8_t *packet = packetArray.ptr();
	int size = (int)packetArray.size();
	if (size < 16) {
		return;
	}
	uint64_t sendTime = read_u64(packet);
	uint64_t senderTick = read_u64(packet + 8);
	int hitBytes = read_hits_from_packet(packet, size, senderTick, sender);
	if (hitBytes < 0) {
		return;
	}
	int offset = 16 + hitBytes;
	while (offset + 9 <= size) {
		uint64_t id = read_u64(packet + offset);
		int stateSize = packet[offset + 8];
		offset += 9;
		if ((stateSize != StateRestingSize && stateSize != StateMovingSize) || offset + stateSize > size) {
			// Not a packet we can read.
			break;
		}
		const uint8_t *stateBytes = packet + offset;
		offset += stateSize;

		auto found = slotById.find(id);
		if (found == slotById.end()) {
			continue;
		}
		int slot = found->second;
		Body &body = bodies[slot];
		// Only the current authority's states count, and never for what this peer simulates itself.
		if (body.authority != (uint64_t)sender || body.authority == selfPeer) {
			continue;
		}
		// Out of order: a newer state is in already.
		if (sendTime < body.lastStateTime) {
			continue;
		}
		body.lastStateTime = sendTime;
		BodyState state = read_body_state(stateBytes, stateSize);

		if (body.snapshotCount > 0) {
			const Snapshot &newest = body.snapshots[body.snapshotCount - 1];
			if (newest.tick >= senderTick) {
				continue;
			}
			// A resting body keeps being sent unchanged: a follower that has already arrived there has nothing to do.
			bool same = newest.position == state.position && newest.rotation == state.rotation &&
					newest.velocity == state.velocity && newest.angularVelocity == state.angularVelocity;
			if (same && !body.following) {
				continue;
			}
		}
		if (body.snapshotCount == MaxSnapshots) {
			for (int i = 1; i < MaxSnapshots; i++) {
				body.snapshots[i - 1] = body.snapshots[i];
			}
			body.snapshotCount--;
		}
		Snapshot &snapshot = body.snapshots[body.snapshotCount];
		snapshot.tick = senderTick;
		snapshot.position = state.position;
		snapshot.rotation = state.rotation;
		snapshot.velocity = state.velocity;
		snapshot.angularVelocity = state.angularVelocity;
		body.snapshotCount++;
		statesApplied++;
		if (!body.following) {
			start_following(slot);
		}
	}
	applyMsTotal += now_ms() - start;
}

void GMPPhysicsWorld::start_following(int slot) {
	Body &body = bodies[slot];
	body.following = true;
	body.blendIn = true;
	if (!body.inFollowers) {
		body.inFollowers = true;
		followers.push_back(slot);
	}
}

// ---- playback, every tick before the step ----

void GMPPhysicsWorld::play_followers(float delta) {
	double start = now_ms();
	// Backwards, so a finished one can be swapped for the last one, which has been played already.
	for (int i = (int)followers.size() - 1; i >= 0; i--) {
		Body &body = bodies[followers[i]];
		if (follow(body, delta)) {
			continue;
		}
		body.inFollowers = false;
		followers[i] = followers.back();
		followers.pop_back();
	}
	followMsTotal += now_ms() - start;
}

// Drives a follower along its authority's states for this tick; false once it has stopped following.
bool GMPPhysicsWorld::follow(Body &body, float delta) {
	if (!body.following || body.snapshotCount == 0) {
		body.following = false;
		return false;
	}
	double interpolationDelay = 2.0 * sendInterval;

	// Asked for this tick or the last.
	bool showingNewest = body.showNewestTick + 1 >= tick;
	double wantedLead = 0;
	if (showingNewest) {
		wantedLead = interpolationDelay;
	}
	// Eased over about half a second, so playback never jumps.
	body.playbackLead = Math::move_toward(body.playbackLead, wantedLead, interpolationDelay * 2 * delta);

	double playbackTick = body.playbackLead;
	auto clock = playbackTicks.find(body.authority);
	if (clock != playbackTicks.end()) {
		playbackTick += clock->second;
	}
	Vector3 position;
	Quaternion rotation;
	bool done = play_back(body, playbackTick, position, rotation);

	if (body.blendIn) {
		// Wherever it was (e.g. simulated here before a hand-off), as an offset that fades out.
		body.blendIn = false;
		body.errorPosition = body.position - position;
		body.errorRotation = body.rotation * rotation.inverse();
		body.blended = false;
	} else if (!body.blended) {
		float keep = std::exp(-delta / ErrorBlendSeconds);
		body.errorPosition *= keep;
		body.errorRotation = Quaternion().slerp(body.errorRotation.normalized(), keep);
	}
	if (!body.blended && body.errorPosition.length() < ArrivedDistance && 1.0f - std::fabs(body.errorRotation.w) < ArrivedRotation) {
		body.blended = true;
		body.errorPosition = Vector3();
		body.errorRotation = Quaternion();
	}

	Vector3 targetPosition = position + body.errorPosition;
	Quaternion targetRotation = rotation;
	if (!body.blended) {
		targetRotation = (body.errorRotation * rotation).normalized();
	}

	if (done && body.blended) {
		// At rest where the authority left it: put it there exactly and stop.
		b3Body_SetTransform(body.body, to_b3(targetPosition), to_b3(targetRotation));
		b3Body_SetLinearVelocity(body.body, b3Vec3{ 0, 0, 0 });
		b3Body_SetAngularVelocity(body.body, b3Vec3{ 0, 0, 0 });
		// A sleeping body sends no move event for this, so it is drawn (and its node put) there from here.
		set_pose(body, targetPosition, targetRotation);
		body.following = false;
		return false;
	}
	// Driven there over this step, with the velocity that takes it there, so it pushes what it meets.
	b3WorldTransform target = { to_b3(targetPosition), to_b3(targetRotation) };
	b3Body_SetTargetTransform(body.body, target, delta, true);
	return true;
}

// The authority's pose at playbackTick from the body's snapshots; also sets its follow velocities. True
// once there is nothing left to play: the newest state is at rest, or has been carried on long enough.
bool GMPPhysicsWorld::play_back(Body &body, double playbackTick, Vector3 &position, Quaternion &rotation) {
	// Drop the states playback has moved past, keeping the newest one at or before it.
	while (body.snapshotCount >= 2 && body.snapshots[1].tick <= playbackTick) {
		for (int i = 1; i < body.snapshotCount; i++) {
			body.snapshots[i - 1] = body.snapshots[i];
		}
		body.snapshotCount--;
	}
	const Snapshot &from = body.snapshots[0];
	if (playbackTick < from.tick) {
		// Every state is still ahead of playback (it has only just started): wait at the first.
		position = from.position;
		rotation = from.rotation;
		body.followVelocity = from.velocity;
		body.followAngularVelocity = from.angularVelocity;
		return false;
	}
	if (body.snapshotCount >= 2) {
		const Snapshot &to = body.snapshots[1];
		float t = (float)((playbackTick - from.tick) / (double)(to.tick - from.tick));
		position = from.position.lerp(to.position, t);
		rotation = from.rotation.slerp(to.rotation, t);
		body.followVelocity = from.velocity.lerp(to.velocity, t);
		body.followAngularVelocity = from.angularVelocity.lerp(to.angularVelocity, t);
		return false;
	}

	// Playback has passed the newest state: carry it on at its velocity, for a little while.
	float ahead = (float)((playbackTick - from.tick) / ticks_per_second());
	float carried = std::min(ahead, MaxExtrapolationSeconds);
	position = from.position + from.velocity * carried;
	rotation = from.rotation;
	Vector3 spin = from.angularVelocity * carried;
	float angle = spin.length();
	if (angle > 1e-5f) {
		// Angular velocity is in world axes, so the turn goes before the body's own rotation.
		rotation = Quaternion(spin / angle, angle) * from.rotation;
	}
	bool atRest = from.velocity == Vector3() && from.angularVelocity == Vector3();
	if (atRest || ahead >= MaxExtrapolationSeconds) {
		body.followVelocity = Vector3();
		body.followAngularVelocity = Vector3();
		return true;
	}
	body.followVelocity = from.velocity;
	body.followAngularVelocity = from.angularVelocity;
	return false;
}
