#pragma once

#include <box3d/math_functions.h>

#include <godot_cpp/variant/transform3d.hpp>

// Conversions between Box3D's and Godot's math types. Both are metres, right handed and Y up,
// so these are plain copies of the numbers.

inline b3Vec3 to_b3(const godot::Vector3 &v) {
	return b3Vec3{ v.x, v.y, v.z };
}

inline godot::Vector3 to_godot(b3Vec3 v) {
	return godot::Vector3(v.x, v.y, v.z);
}

// Box3D checks that rotations are normalized to within a few millionths, tighter than rotations read
// off the wire or worked out from a basis, so every one is normalized on the way in.
inline b3Quat to_b3(const godot::Quaternion &q) {
	godot::Quaternion n = q.normalized();
	return b3Quat{ { n.x, n.y, n.z }, n.w };
}

inline godot::Quaternion to_godot(b3Quat q) {
	return godot::Quaternion(q.v.x, q.v.y, q.v.z, q.s);
}

inline godot::Transform3D to_godot(const b3WorldTransform &t) {
	return godot::Transform3D(godot::Basis(to_godot(t.q)), godot::Vector3(t.p.x, t.p.y, t.p.z));
}
