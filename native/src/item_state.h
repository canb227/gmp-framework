#pragma once

#include <godot_cpp/core/math.hpp>
#include <godot_cpp/variant/quaternion.hpp>
#include <godot_cpp/variant/vector3.hpp>

#include <cmath>
#include <cstdint>
#include <cstring>

// One item's state on the wire: the same layout as BodySyncState in C# (gmp/sync/SyncHelpers.cs).
//
//   position          3 floats                                       12 bytes
//   rotation          the three smaller quaternion parts as shorts,   7 bytes
//                     then which part was left out (0-3)
//   velocity          3 half floats, only when moving                  6 bytes
//   angular velocity  3 half floats, only when moving                  6 bytes
//
// The largest quaternion part is left out: the other three give it back, as the quaternion has length 1,
// and with it made positive (the same rotation) it needs no sign. The others are at most 1/sqrt(2), so
// times ItemStatePartScale they fit a short. A resting state has no velocities: 19 bytes, else 31.

struct ItemState {
	godot::Vector3 position;
	godot::Quaternion rotation;
	godot::Vector3 velocity;
	godot::Vector3 angularVelocity;
};

const int ItemStateRestingSize = 19;
const int ItemStateMovingSize = 31;
const float ItemStatePartScale = 32767.0f * 1.41421356f;

inline void write_float(uint8_t *to, float value) {
	std::memcpy(to, &value, 4);
}

inline float read_float(const uint8_t *from) {
	float value;
	std::memcpy(&value, from, 4);
	return value;
}

inline void write_short(uint8_t *to, int16_t value) {
	std::memcpy(to, &value, 2);
}

inline int16_t read_short(const uint8_t *from) {
	int16_t value;
	std::memcpy(&value, from, 2);
	return value;
}

inline void write_half(uint8_t *to, float value) {
	uint16_t half = godot::Math::make_half_float(value);
	std::memcpy(to, &half, 2);
}

inline float read_half(const uint8_t *from) {
	uint16_t half;
	std::memcpy(&half, from, 2);
	return godot::Math::half_to_float(half);
}

inline void write_u64(uint8_t *to, uint64_t value) {
	std::memcpy(to, &value, 8);
}

inline uint64_t read_u64(const uint8_t *from) {
	uint64_t value;
	std::memcpy(&value, from, 8);
	return value;
}

// Writes the state and returns how many bytes it took (ItemStateRestingSize or ItemStateMovingSize).
inline int write_item_state(uint8_t *to, const ItemState &state) {
	write_float(to + 0, state.position.x);
	write_float(to + 4, state.position.y);
	write_float(to + 8, state.position.z);

	godot::Quaternion q = state.rotation.normalized();
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
			float scaled = std::round(parts[i] * ItemStatePartScale);
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

	bool resting = state.velocity == godot::Vector3() && state.angularVelocity == godot::Vector3();
	if (resting) {
		return ItemStateRestingSize;
	}
	write_half(to + 19, state.velocity.x);
	write_half(to + 21, state.velocity.y);
	write_half(to + 23, state.velocity.z);
	write_half(to + 25, state.angularVelocity.x);
	write_half(to + 27, state.angularVelocity.y);
	write_half(to + 29, state.angularVelocity.z);
	return ItemStateMovingSize;
}

// Reads a state of size bytes (ItemStateRestingSize or ItemStateMovingSize).
inline ItemState read_item_state(const uint8_t *from, int size) {
	ItemState state;
	state.position = godot::Vector3(read_float(from + 0), read_float(from + 4), read_float(from + 8));

	int largest = from[18] & 3;
	float parts[4] = { 0, 0, 0, 0 };
	float sumOfSquares = 0;
	int read = 0;
	for (int i = 0; i < 4; i++) {
		if (i != largest) {
			parts[i] = read_short(from + 12 + 2 * read) / ItemStatePartScale;
			sumOfSquares += parts[i] * parts[i];
			read++;
		}
	}
	float rest = 1.0f - sumOfSquares;
	if (rest < 0) {
		rest = 0;
	}
	parts[largest] = std::sqrt(rest);
	state.rotation = godot::Quaternion(parts[0], parts[1], parts[2], parts[3]);

	if (size >= ItemStateMovingSize) {
		state.velocity = godot::Vector3(read_half(from + 19), read_half(from + 21), read_half(from + 23));
		state.angularVelocity = godot::Vector3(read_half(from + 25), read_half(from + 27), read_half(from + 29));
	}
	return state;
}
