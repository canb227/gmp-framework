#pragma once

#include <cstdint>

class GMPBody;

// What a Box3D body belongs to, kept in the body's userData so events and queries can tell straight
// away without a lookup:
//   an item       (slot << 1) | 1, an odd number: slot is the item's place in GMPPhysicsWorld::items
//   a GMPBody     the node's pointer, an even number (objects are always at even addresses)
//   nothing       0

inline void *user_data_for_item(int slot) {
	return (void *)(((uintptr_t)slot << 1) | 1);
}

inline void *user_data_for_node(GMPBody *body) {
	return (void *)body;
}

inline bool user_data_is_item(void *userData) {
	return ((uintptr_t)userData & 1) == 1;
}

inline int item_slot_from_user_data(void *userData) {
	return (int)((uintptr_t)userData >> 1);
}

// nullptr when the body is an item's or nobody's.
inline GMPBody *node_from_user_data(void *userData) {
	if (userData == nullptr || user_data_is_item(userData)) {
		return nullptr;
	}
	return (GMPBody *)userData;
}
