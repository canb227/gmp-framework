// GMPPhysicsWorld: networking the items. See physics_world.h.
//
// Each item has one authority, the peer that simulates it (the host, for every loose item). The authority
// sends its state; every other peer follows: the item's body is kinematic there, and is driven along the
// authority's states, played back about two sends behind the newest so packets arriving unevenly don't
// show. C# moves the packets (GameWorld) and keeps the playback clock per sending peer (set_playback_tick).
//
// Sending: on each send, every item whose turn it is (id % send interval) adds its priority to its
// accumulator, and the states go out highest accumulator first until the budget for this send is used up.
// A sent item starts again from 0, so everything gets sent eventually, the important things more often.
// An item at rest stops sending once its resting pose has gone out a few times, until it moves.
//
// A packet:   send time (8 bytes, GameWorld.netTime)   sender's physics tick (8 bytes)
//             then per item: id (8 bytes), state size (1 byte), state (item_state.h)

#include "physics_world.h"

#include "math_convert.h"

#include <godot_cpp/classes/engine.hpp>

#include <algorithm>
#include <chrono>
#include <cmath>

using namespace godot;

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
// Times the priority of an item inside any player's view cone (about 57 degrees either side of where they
// look), so what someone looks at is updated more often than what's behind them.
const float ViewMultiplier = 2.5f;
const float ViewConeCos = 0.55f;
// Priority of an item a magnet rod holds: above any nearness bonus, below the players.
const int HeldPriority = 100;
// Times a resting pose is sent before the item goes quiet (each is unreliable), and how often a quiet
// item still sends its pose, in case every copy of it was lost.
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

void GMPPhysicsWorld::hold_item(int64_t id) {
	auto found = slotById.find((uint64_t)id);
	if (found != slotById.end()) {
		items[found->second].heldUntilTick = tick + ticks_per_second();
	}
}

void GMPPhysicsWorld::show_item_newest(int64_t id) {
	auto found = slotById.find((uint64_t)id);
	if (found != slotById.end()) {
		items[found->second].showNewestTick = tick;
	}
}

void GMPPhysicsWorld::set_playback_tick(int64_t peer, double playbackTick) {
	playbackTicks[(uint64_t)peer] = playbackTick;
}

void GMPPhysicsWorld::set_item_authority(int64_t id, int64_t peer) {
	auto found = slotById.find((uint64_t)id);
	if (found == slotById.end()) {
		return;
	}
	Item &item = items[found->second];
	item.authority = (uint64_t)peer;
	// The new authority's followers need its resting pose again, and its ticks are counted by its own clock.
	item.lastBuiltResting = false;
	item.snapshotCount = 0;
	item.lastStateTime = 0;
	item.following = false;
	if (item.authority == selfPeer) {
		// A follower is only ever driven along, so it carries on at the velocity it was followed at.
		b3Body_SetType(item.body, b3_dynamicBody);
		b3Body_SetLinearVelocity(item.body, to_b3(item.followVelocity));
		b3Body_SetAngularVelocity(item.body, to_b3(item.followAngularVelocity));
		b3Body_SetAwake(item.body, true);
		item.asleep = false;
	} else {
		b3Body_SetType(item.body, b3_kinematicBody);
		b3Body_SetLinearVelocity(item.body, b3Vec3{ 0, 0, 0 });
		b3Body_SetAngularVelocity(item.body, b3Vec3{ 0, 0, 0 });
	}
}

// True while the item is at rest, its resting pose has gone out RestingSends times and it hasn't moved
// since: a send would only repeat it.
bool GMPPhysicsWorld::at_rest_and_sent(const Item &item, uint64_t sendTick) const {
	if (!item.lastBuiltResting || item.restingSendsLeft > 0) {
		return false;
	}
	if (sendTick - item.lastBuiltSendTick >= (uint64_t)(RestingHeartbeatSeconds * ticks_per_second())) {
		return false;
	}
	return item.position == item.lastBuiltPosition && item.rotation == item.lastBuiltRotation;
}

int GMPPhysicsWorld::item_priority(Item &item, uint64_t sendTick) {
	int basePriority = kinds[item.kind].basePriority;
	if (basePriority <= -1 || at_rest_and_sent(item, sendTick)) {
		return -1;
	}
	int unscaled = basePriority;
	if (!item.asleep) {
		unscaled += 1;
	}
	float nearestSquared = ProximityRange * ProximityRange;
	for (const Vector3 &viewer : viewerPositions) {
		float distanceSquared = item.position.distance_squared_to(viewer);
		if (distanceSquared < nearestSquared) {
			nearestSquared = distanceSquared;
		}
	}
	float nearest = std::sqrt(nearestSquared);
	float multiplier = 1.0f + (ProximityMultiplier - 1.0f) * (1.0f - nearest / ProximityRange);
	for (int i = 0; i < (int)viewerEyes.size(); i++) {
		Vector3 toItem = item.position - viewerEyes[i];
		float distance = toItem.length();
		if (distance > 0 && viewerLooks[i].dot(toItem) >= ViewConeCos * distance) {
			multiplier *= ViewMultiplier;
			break;
		}
	}
	int priority = (int)std::round(unscaled * multiplier);
	if (tick < item.heldUntilTick && priority < HeldPriority) {
		priority = HeldPriority;
	}
	return priority;
}

ItemState GMPPhysicsWorld::build_item_state(Item &item, uint64_t sendTick) {
	ItemState state;
	state.position = item.position;
	state.rotation = item.rotation;
	// At rest it goes out without velocities, so followers don't drift on whatever it fell asleep with.
	bool resting = item.asleep;
	if (!resting) {
		state.velocity = to_godot(b3Body_GetLinearVelocity(item.body));
		state.angularVelocity = to_godot(b3Body_GetAngularVelocity(item.body));
	}
	bool unmoved = item.position == item.lastBuiltPosition && item.rotation == item.lastBuiltRotation;
	if (resting && item.lastBuiltResting && unmoved) {
		item.restingSendsLeft = std::max(0, item.restingSendsLeft - 1);
	} else if (resting) {
		item.restingSendsLeft = RestingSends - 1;
	}
	item.lastBuiltPosition = item.position;
	item.lastBuiltRotation = item.rotation;
	item.lastBuiltResting = resting;
	item.lastBuiltSendTick = sendTick;
	return state;
}

static PackedByteArray to_packet(const std::vector<uint8_t> &bytes) {
	PackedByteArray packet;
	packet.resize((int64_t)bytes.size());
	std::memcpy(packet.ptrw(), bytes.data(), bytes.size());
	return packet;
}

Array GMPPhysicsWorld::build_item_states(int maxBytes, int maxPacketBytes, int turn, int64_t sendTick, int64_t sendTime) {
	double start = now_ms();
	Array packets;

	std::vector<int> candidates;
	for (int slot = 0; slot < (int)items.size(); slot++) {
		Item &item = items[slot];
		if (item.id == 0 || item.authority != selfPeer || (int)(item.id % (uint64_t)sendInterval) != turn) {
			continue;
		}
		int priority = item_priority(item, (uint64_t)sendTick);
		if (priority <= -1) {
			continue;
		}
		item.priorityAccumulator += priority;
		candidates.push_back(slot);
	}
	// Highest accumulator first (std::sort takes the comparison as a function).
	std::sort(candidates.begin(), candidates.end(), [this](int a, int b) {
		return items[a].priorityAccumulator > items[b].priorityAccumulator;
	});

	const int headerSize = 16;
	std::vector<uint8_t> packet(headerSize);
	write_u64(packet.data(), (uint64_t)sendTime);
	write_u64(packet.data() + 8, (uint64_t)sendTick);
	int sentBytes = 0;
	for (int slot : candidates) {
		Item &item = items[slot];
		ItemState state = build_item_state(item, (uint64_t)sendTick);
		statesBuilt++;
		uint8_t stateBytes[ItemStateMovingSize];
		int stateSize = write_item_state(stateBytes, state);
		int entrySize = 8 + 1 + stateSize;
		if (sentBytes + entrySize > maxBytes) {
			// This send is full: the rest wait for the next one (without being built).
			break;
		}
		if ((int)packet.size() > headerSize && (int)packet.size() + entrySize > maxPacketBytes) {
			packets.push_back(to_packet(packet));
			packet.resize(headerSize);
		}
		size_t at = packet.size();
		packet.resize(at + entrySize);
		write_u64(packet.data() + at, item.id);
		packet[at + 8] = (uint8_t)stateSize;
		std::memcpy(packet.data() + at + 9, stateBytes, stateSize);
		sentBytes += entrySize;
		item.priorityAccumulator = 0;
	}
	if ((int)packet.size() > headerSize) {
		packets.push_back(to_packet(packet));
	}
	buildMsTotal += now_ms() - start;
	return packets;
}

// ---- receiving ----

void GMPPhysicsWorld::apply_item_states(PackedByteArray packetArray, int64_t sender) {
	double start = now_ms();
	const uint8_t *packet = packetArray.ptr();
	int size = (int)packetArray.size();
	if (size < 16) {
		return;
	}
	uint64_t sendTime = read_u64(packet);
	uint64_t senderTick = read_u64(packet + 8);
	int offset = 16;
	while (offset + 9 <= size) {
		uint64_t id = read_u64(packet + offset);
		int stateSize = packet[offset + 8];
		offset += 9;
		if ((stateSize != ItemStateRestingSize && stateSize != ItemStateMovingSize) || offset + stateSize > size) {
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
		Item &item = items[slot];
		// Only the current authority's states count, and never for what this peer simulates itself.
		if (item.authority != (uint64_t)sender || item.authority == selfPeer) {
			continue;
		}
		// Out of order: a newer state is in already.
		if (sendTime < item.lastStateTime) {
			continue;
		}
		item.lastStateTime = sendTime;
		ItemState state = read_item_state(stateBytes, stateSize);

		if (item.snapshotCount > 0) {
			const ItemSnapshot &newest = item.snapshots[item.snapshotCount - 1];
			if (newest.tick >= senderTick) {
				continue;
			}
			// A resting item keeps being sent unchanged: a follower that has already arrived there has nothing to do.
			bool same = newest.position == state.position && newest.rotation == state.rotation &&
					newest.velocity == state.velocity && newest.angularVelocity == state.angularVelocity;
			if (same && !item.following) {
				continue;
			}
		}
		if (item.snapshotCount == MaxItemSnapshots) {
			for (int i = 1; i < MaxItemSnapshots; i++) {
				item.snapshots[i - 1] = item.snapshots[i];
			}
			item.snapshotCount--;
		}
		ItemSnapshot &snapshot = item.snapshots[item.snapshotCount];
		snapshot.tick = senderTick;
		snapshot.position = state.position;
		snapshot.rotation = state.rotation;
		snapshot.velocity = state.velocity;
		snapshot.angularVelocity = state.angularVelocity;
		item.snapshotCount++;
		statesApplied++;
		if (!item.following) {
			start_following(slot);
		}
	}
	applyMsTotal += now_ms() - start;
}

void GMPPhysicsWorld::start_following(int slot) {
	Item &item = items[slot];
	item.following = true;
	item.blendIn = true;
	if (!item.inFollowers) {
		item.inFollowers = true;
		followers.push_back(slot);
	}
}

// ---- playback, every tick before the step ----

void GMPPhysicsWorld::play_followers(float delta) {
	double start = now_ms();
	// Backwards, so a finished one can be swapped for the last one, which has been played already.
	for (int i = (int)followers.size() - 1; i >= 0; i--) {
		Item &item = items[followers[i]];
		if (follow(item, delta)) {
			continue;
		}
		item.inFollowers = false;
		followers[i] = followers.back();
		followers.pop_back();
	}
	followMsTotal += now_ms() - start;
}

// Drives a follower along its authority's states for this tick; false once it has stopped following.
bool GMPPhysicsWorld::follow(Item &item, float delta) {
	if (!item.following || item.snapshotCount == 0) {
		item.following = false;
		return false;
	}
	double interpolationDelay = 2.0 * sendInterval;

	// Asked for this tick or the last.
	bool showingNewest = item.showNewestTick + 1 >= tick;
	double wantedLead = 0;
	if (showingNewest) {
		wantedLead = interpolationDelay;
	}
	// Eased over about half a second, so playback never jumps.
	item.playbackLead = Math::move_toward(item.playbackLead, wantedLead, interpolationDelay * 2 * delta);

	double playbackTick = item.playbackLead;
	auto clock = playbackTicks.find(item.authority);
	if (clock != playbackTicks.end()) {
		playbackTick += clock->second;
	}
	Vector3 position;
	Quaternion rotation;
	bool done = play_back(item, playbackTick, position, rotation);

	if (item.blendIn) {
		// Wherever it was (e.g. simulated here before a hand-off), as an offset that fades out.
		item.blendIn = false;
		item.errorPosition = item.position - position;
		item.errorRotation = item.rotation * rotation.inverse();
		item.blended = false;
	} else if (!item.blended) {
		float keep = std::exp(-delta / ErrorBlendSeconds);
		item.errorPosition *= keep;
		item.errorRotation = Quaternion().slerp(item.errorRotation.normalized(), keep);
	}
	if (!item.blended && item.errorPosition.length() < ArrivedDistance && 1.0f - std::fabs(item.errorRotation.w) < ArrivedRotation) {
		item.blended = true;
		item.errorPosition = Vector3();
		item.errorRotation = Quaternion();
	}

	Vector3 targetPosition = position + item.errorPosition;
	Quaternion targetRotation = rotation;
	if (!item.blended) {
		targetRotation = (item.errorRotation * rotation).normalized();
	}

	if (done && item.blended) {
		// At rest where the authority left it: put it there exactly and stop.
		b3Body_SetTransform(item.body, to_b3(targetPosition), to_b3(targetRotation));
		b3Body_SetLinearVelocity(item.body, b3Vec3{ 0, 0, 0 });
		b3Body_SetAngularVelocity(item.body, b3Vec3{ 0, 0, 0 });
		// A sleeping body sends no move event for this, so it is drawn there from here.
		item.previous = item.current;
		item.position = targetPosition;
		item.rotation = targetRotation;
		item.current = Transform3D(Basis(targetRotation), targetPosition);
		item.movedTick = tick;
		kinds[item.kind].changedTick = tick;
		item.following = false;
		return false;
	}
	// Driven there over this step, with the velocity that takes it there, so it pushes what it meets.
	b3WorldTransform target = { to_b3(targetPosition), to_b3(targetRotation) };
	b3Body_SetTargetTransform(item.body, target, delta, true);
	return true;
}

// The authority's pose at playbackTick from the item's snapshots; also sets its follow velocities. True
// once there is nothing left to play: the newest state is at rest, or has been carried on long enough.
bool GMPPhysicsWorld::play_back(Item &item, double playbackTick, Vector3 &position, Quaternion &rotation) {
	// Drop the states playback has moved past, keeping the newest one at or before it.
	while (item.snapshotCount >= 2 && item.snapshots[1].tick <= playbackTick) {
		for (int i = 1; i < item.snapshotCount; i++) {
			item.snapshots[i - 1] = item.snapshots[i];
		}
		item.snapshotCount--;
	}
	const ItemSnapshot &from = item.snapshots[0];
	if (playbackTick < from.tick) {
		// Every state is still ahead of playback (it has only just started): wait at the first.
		position = from.position;
		rotation = from.rotation;
		item.followVelocity = from.velocity;
		item.followAngularVelocity = from.angularVelocity;
		return false;
	}
	if (item.snapshotCount >= 2) {
		const ItemSnapshot &to = item.snapshots[1];
		float t = (float)((playbackTick - from.tick) / (double)(to.tick - from.tick));
		position = from.position.lerp(to.position, t);
		rotation = from.rotation.slerp(to.rotation, t);
		item.followVelocity = from.velocity.lerp(to.velocity, t);
		item.followAngularVelocity = from.angularVelocity.lerp(to.angularVelocity, t);
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
		// Angular velocity is in world axes, so the turn goes before the item's own rotation.
		rotation = Quaternion(spin / angle, angle) * from.rotation;
	}
	bool atRest = from.velocity == Vector3() && from.angularVelocity == Vector3();
	if (atRest || ahead >= MaxExtrapolationSeconds) {
		item.followVelocity = Vector3();
		item.followAngularVelocity = Vector3();
		return true;
	}
	item.followVelocity = from.velocity;
	item.followAngularVelocity = from.angularVelocity;
	return false;
}
