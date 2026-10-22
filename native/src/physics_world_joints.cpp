// GMPPhysicsWorld: joints between two bodies, by their ids. See physics_world.h.
//
// A joint's handle is its Box3D id packed into an integer (b3StoreJointId), so there is no table of them to keep.
// Box3D destroys a body's joints along with it, so every call here checks the joint is still there first, and a
// handle of 0 is never a joint.
//
// Make joints on every peer: a joint only acts while one of its bodies is dynamic, so on a peer that follows both
// bodies it does nothing, and it starts working by itself should that peer become their authority.

#include "physics_world.h"

#include "math_convert.h"

using namespace godot;

int64_t GMPPhysicsWorld::create_distance_joint(int64_t idA, Vector3 anchorA, int64_t idB, Vector3 anchorB, float minLength, float maxLength) {
	Body *a = find_body(idA);
	Body *b = find_body(idB);
	if (a == nullptr || b == nullptr) {
		return 0;
	}
	b3DistanceJointDef def = b3DefaultDistanceJointDef();
	def.base.bodyIdA = a->body;
	def.base.bodyIdB = b->body;
	def.base.localFrameA = b3Transform{ to_b3(anchorA), b3Quat_identity };
	def.base.localFrameB = b3Transform{ to_b3(anchorB), b3Quat_identity };
	def.length = maxLength;
	// A spring with no stiffness: only the limits hold, so the joint goes slack anywhere between them (a rope from 0
	// up to its length). Box3D's rigid distance joint (no spring) would ignore the limits.
	def.enableSpring = true;
	def.hertz = 0;
	def.enableLimit = true;
	def.minLength = minLength;
	def.maxLength = maxLength;
	b3JointId joint = b3CreateDistanceJoint(world, &def);
	return (int64_t)b3StoreJointId(joint);
}

void GMPPhysicsWorld::set_distance_joint_range(int64_t joint, float minLength, float maxLength) {
	b3JointId id = b3LoadJointId((uint64_t)joint);
	if (joint == 0 || !b3Joint_IsValid(id)) {
		return;
	}
	b3DistanceJoint_SetLength(id, maxLength);
	b3DistanceJoint_SetLengthRange(id, minLength, maxLength);
	b3Joint_WakeBodies(id);
}

int64_t GMPPhysicsWorld::create_filter_joint(int64_t idA, int64_t idB) {
	Body *a = find_body(idA);
	Body *b = find_body(idB);
	if (a == nullptr || b == nullptr) {
		return 0;
	}
	b3FilterJointDef def = b3DefaultFilterJointDef();
	def.base.bodyIdA = a->body;
	def.base.bodyIdB = b->body;
	b3JointId joint = b3CreateFilterJoint(world, &def);
	return (int64_t)b3StoreJointId(joint);
}

void GMPPhysicsWorld::destroy_joint(int64_t joint) {
	b3JointId id = b3LoadJointId((uint64_t)joint);
	if (joint == 0 || !b3Joint_IsValid(id)) {
		return;
	}
	b3DestroyJoint(id, true);
}

static b3Transform to_b3_frame(const Transform3D &frame) {
	return b3Transform{ to_b3(frame.origin), to_b3(frame.basis.get_rotation_quaternion()) };
}

int64_t GMPPhysicsWorld::create_revolute_joint(int64_t idA, Transform3D frameA, int64_t idB, Transform3D frameB, float lowerAngle, float upperAngle) {
	Body *a = find_body(idA);
	Body *b = find_body(idB);
	if (a == nullptr || b == nullptr) {
		return 0;
	}
	b3RevoluteJointDef def = b3DefaultRevoluteJointDef();
	def.base.bodyIdA = a->body;
	def.base.bodyIdB = b->body;
	def.base.localFrameA = to_b3_frame(frameA);
	def.base.localFrameB = to_b3_frame(frameB);
	def.enableLimit = lowerAngle < upperAngle;
	def.lowerAngle = lowerAngle;
	def.upperAngle = upperAngle;
	// The motor is on from the start, doing nothing until set_revolute_joint_motor gives it a torque.
	def.enableMotor = true;
	def.maxMotorTorque = 0;
	def.motorSpeed = 0;
	b3JointId joint = b3CreateRevoluteJoint(world, &def);
	return (int64_t)b3StoreJointId(joint);
}

void GMPPhysicsWorld::set_revolute_joint_motor(int64_t joint, float speed, float maxTorque) {
	b3JointId id = b3LoadJointId((uint64_t)joint);
	if (joint == 0 || !b3Joint_IsValid(id)) {
		return;
	}
	// Only a change wakes the bodies, so a motor held still lets them sleep.
	if (b3RevoluteJoint_GetMotorSpeed(id) == speed && b3RevoluteJoint_GetMaxMotorTorque(id) == maxTorque) {
		return;
	}
	b3RevoluteJoint_SetMotorSpeed(id, speed);
	b3RevoluteJoint_SetMaxMotorTorque(id, maxTorque);
	b3Joint_WakeBodies(id);
}

float GMPPhysicsWorld::get_revolute_joint_angle(int64_t joint) {
	b3JointId id = b3LoadJointId((uint64_t)joint);
	if (joint == 0 || !b3Joint_IsValid(id)) {
		return 0;
	}
	return b3RevoluteJoint_GetAngle(id);
}
