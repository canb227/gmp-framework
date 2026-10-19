#include "physics_character.h"

#include "body_user_data.h"
#include "math_convert.h"
#include "physics_world.h"

#include <godot_cpp/core/class_db.hpp>

#include <cmath>
#include <cstring>

using namespace godot;

// Queries leave out this layer (GameWorld.QueryHiddenLayer in C#).
const uint64_t QueryHiddenLayer = 1ull << 30;
// How much a soft plane (a loose item, a dynamic body) may push the mover back per solve, in metres: a
// little, so the character slows as it wades into a pile while its body shoves the pile aside.
const float SoftPushLimit = 0.01f;
const int MaxMoverPlanes = 32;
const int MoverIterations = 5;

GMPCharacter::GMPCharacter() {
	bodyType = KINEMATIC;
	canSleep = false;
	// Players are on their own layer, 3 (bit 2).
	collisionLayer = 1 << 2;
}

void GMPCharacter::_notification(int what) {
	// GMPBody's own handling of these runs first and makes (or drops) the body.
	if (what == NOTIFICATION_READY || (what == NOTIFICATION_ENTER_TREE && is_node_ready())) {
		make_capsule_shape();
	}
	if (what == NOTIFICATION_EXIT_TREE) {
		// It went with the body.
		capsuleShape = b3_nullShapeId;
	}
}

b3Capsule GMPCharacter::capsule() const {
	float bottom = stepHeight + radius;
	float top = height - radius;
	if (top < bottom) {
		top = bottom;
	}
	return b3Capsule{ { 0, bottom, 0 }, { 0, top, 0 }, radius };
}

void GMPCharacter::make_capsule_shape() {
	if (!has_body()) {
		return;
	}
	if (b3Shape_IsValid(capsuleShape)) {
		b3DestroyShape(capsuleShape, false);
	}
	b3ShapeDef def = b3DefaultShapeDef();
	def.filter.categoryBits = collisionLayer;
	def.filter.maskBits = collisionMask;
	// Machines' sensors and the like can see characters.
	def.enableSensorEvents = true;
	b3Capsule shape = capsule();
	capsuleShape = b3CreateCapsuleShape(get_body_id(), &def, &shape);
}

// ---- what the mover and the legs collide with ----

struct MoverContext {
	b3BodyId self = b3_nullBodyId;
	b3CollisionPlane planes[MaxMoverPlanes];
	// The shape each plane is on.
	b3ShapeId planeShapes[MaxMoverPlanes];
	int planeCount = 0;
	// The legs' cast: the closest hit.
	bool hit = false;
	b3ShapeId hitShape = b3_nullShapeId;
	b3Pos hitPoint;
	b3Vec3 hitNormal;
	float hitFraction = 1;
};

static bool is_self(const MoverContext *context, b3ShapeId shapeId) {
	return B3_ID_EQUALS(b3Shape_GetBody(shapeId), context->self);
}

// Loose items and dynamic bodies give way: the character presses into them while its body shoves them.
static bool is_soft(b3ShapeId shapeId) {
	b3BodyId body = b3Shape_GetBody(shapeId);
	return user_data_is_item(b3Body_GetUserData(body)) || b3Body_GetType(body) == b3_dynamicBody;
}

static bool mover_filter(b3ShapeId shapeId, void *context) {
	return !is_self((MoverContext *)context, shapeId) && !b3Shape_IsSensor(shapeId);
}

static bool collect_planes(b3ShapeId shapeId, const b3PlaneResult *results, int count, void *context) {
	MoverContext *mover = (MoverContext *)context;
	if (!mover_filter(shapeId, context)) {
		// Not these, but keep looking.
		return true;
	}
	bool soft = is_soft(shapeId);
	for (int i = 0; i < count && mover->planeCount < MaxMoverPlanes; i++) {
		b3CollisionPlane &plane = mover->planes[mover->planeCount];
		mover->planeShapes[mover->planeCount] = shapeId;
		plane.plane = results[i].plane;
		plane.push = 0;
		if (soft) {
			plane.pushLimit = SoftPushLimit;
			plane.clipVelocity = false;
		} else {
			plane.pushLimit = FLT_MAX;
			plane.clipVelocity = true;
		}
		mover->planeCount++;
	}
	return true;
}

static float legs_callback(b3ShapeId shapeId, b3Pos point, b3Vec3 normal, float fraction, uint64_t userMaterialId,
		int triangleIndex, int childIndex, void *context) {
	MoverContext *mover = (MoverContext *)context;
	if (!mover_filter(shapeId, context)) {
		return -1.0f;
	}
	mover->hit = true;
	mover->hitShape = shapeId;
	mover->hitPoint = point;
	mover->hitNormal = normal;
	mover->hitFraction = fraction;
	// Only closer hits from here on.
	return fraction;
}

static bool any_rigid_overlap(b3ShapeId shapeId, void *context) {
	MoverContext *mover = (MoverContext *)context;
	if (!mover_filter(shapeId, context) || is_soft(shapeId)) {
		return true;
	}
	mover->hit = true;
	// Found one: stop.
	return false;
}

static b3QueryFilter mover_query_filter(uint32_t mask) {
	b3QueryFilter filter = b3DefaultQueryFilter();
	filter.maskBits = mask & ~QueryHiddenLayer;
	return filter;
}

// ---- the legs ----

void GMPCharacter::probe_floor(b3WorldId world, b3Pos feet, bool rising, float &distance) {
	// A ball a little thinner than the capsule, from the bottom of the capsule straight down past the feet.
	float ballRadius = radius * 0.9f;
	float restTravel = stepHeight + radius - ballRadius;
	float length = restTravel + groundSnap;
	b3Pos start = { feet.x, feet.y + stepHeight + radius, feet.z };
	b3Vec3 centre = { 0, 0, 0 };
	b3ShapeProxy ball = { &centre, 1, ballRadius };

	MoverContext context;
	context.self = get_body_id();
	b3World_CastShape(world, start, &ball, b3Vec3{ 0, -length, 0 }, mover_query_filter(collisionMask), legs_callback, &context);

	onFloor = false;
	floorNormal = Vector3(0, 1, 0);
	floorVelocity = Vector3();
	distance = 0;
	if (!context.hit || rising) {
		return;
	}
	float minNormalY = std::cos(maxSlopeDegrees * B3_PI / 180.0f);
	if (context.hitNormal.y < minNormalY) {
		// Too steep to stand on.
		return;
	}
	onFloor = true;
	floorNormal = to_godot(context.hitNormal);
	floorPoint = Vector3(context.hitPoint.x, context.hitPoint.y, context.hitPoint.z);
	floorShape = context.hitShape;
	distance = context.hitFraction * length - restTravel;

	// Standing still on something moving means moving with it: a body's own motion at that point, plus a
	// belt's surface drive (in the body's axes).
	b3BodyId ground = b3Shape_GetBody(context.hitShape);
	b3Vec3 pointVelocity = b3Body_GetWorldPointVelocity(ground, context.hitPoint);
	b3SurfaceMaterial material = b3Shape_GetSurfaceMaterial(context.hitShape);
	b3Vec3 drive = b3RotateVector(b3Body_GetRotation(ground), material.tangentVelocity);
	floorVelocity = to_godot(pointVelocity) + to_godot(drive);
}

// ---- moving ----

Vector3 GMPCharacter::move(Vector3 velocity, float delta) {
	Transform3D transform = get_global_transform();
	if (!has_body() || delta <= 0) {
		transform.origin += velocity * delta;
		set_global_transform(transform);
		return velocity;
	}
	b3WorldId world = b3Body_GetWorld(get_body_id());
	b3Pos feet = to_b3(transform.origin);
	impacts.clear();
	// As the last move left it.
	bool wasOnFloor = onFloor;
	Vector3 velocityIn = velocity;

	// Jumping: the legs let go.
	bool rising = velocity.y > 0.5f;
	float distance = 0;
	probe_floor(world, feet, rising, distance);
	if (onFloor) {
		// The legs' spring (implicit, so it never overshoots), pulling the feet to standing height.
		float zeta = 1.0f;
		float omega = 2.0f * B3_PI * legHertz;
		float omegaH = omega * delta;
		legVelocity = (legVelocity - omega * omegaH * distance) / (1.0f + 2.0f * zeta * omegaH + omegaH * omegaH);
	} else {
		legVelocity = 0;
	}

	b3Vec3 step = to_b3(velocity * delta);
	b3Pos target = { feet.x + step.x, feet.y + step.y + legVelocity * delta, feet.z + step.z };
	b3Capsule shape = capsule();
	b3QueryFilter filter = mover_query_filter(collisionMask);

	MoverContext context;
	context.self = get_body_id();
	for (int iteration = 0; iteration < MoverIterations; iteration++) {
		context.planeCount = 0;
		b3World_CollideMover(world, feet, &shape, filter, collect_planes, &context);
		b3Vec3 wanted = { target.x - feet.x, target.y - feet.y, target.z - feet.z };
		b3PlaneSolverResult solved = b3SolvePlanes(wanted, context.planes, context.planeCount);
		float fraction = b3World_CastMover(world, feet, &shape, solved.delta, filter, mover_filter, &context);
		b3Vec3 moved = { solved.delta.x * fraction, solved.delta.y * fraction, solved.delta.z * fraction };
		feet = { feet.x + moved.x, feet.y + moved.y, feet.z + moved.z };
		if (moved.x * moved.x + moved.y * moved.y + moved.z * moved.z < 0.0001f) {
			break;
		}
	}
	// What the solid planes it ended against leave of the velocity (soft ones don't take any).
	velocity = to_godot(b3ClipVector(to_b3(velocity), context.planes, context.planeCount));

	// Running into a wall or ceiling: the solid plane too steep to stand on that it went into fastest, if it
	// wasn't against one already.
	float minNormalY = std::cos(maxSlopeDegrees * B3_PI / 180.0f);
	bool wall = false;
	float bumpSpeed = 0;
	int bumpPlane = -1;
	for (int i = 0; i < context.planeCount; i++) {
		const b3CollisionPlane &plane = context.planes[i];
		if (!plane.clipVelocity || plane.plane.normal.y >= minNormalY) {
			continue;
		}
		wall = true;
		float speed = -velocityIn.dot(to_godot(plane.plane.normal));
		if (speed > bumpSpeed) {
			bumpSpeed = speed;
			bumpPlane = i;
		}
	}
	Vector3 feetAfter = Vector3(feet.x, feet.y, feet.z);
	if (wall && !againstWall && bumpPlane >= 0) {
		Vector3 normal = to_godot(context.planes[bumpPlane].plane.normal);
		// Where the capsule's middle meets it.
		Vector3 middle = feetAfter + Vector3(0, (stepHeight + height) * 0.5f, 0);
		add_impact(1, middle - normal * radius, normal, bumpSpeed, context.planeShapes[bumpPlane]);
	}
	againstWall = wall;

	transform.origin = feetAfter;
	set_global_transform(transform);
	// Ready for the next move, and for C# to read in between.
	probe_floor(world, feet, rising, distance);
	if (onFloor && !wasOnFloor) {
		float speed = -velocityIn.dot(floorNormal);
		if (speed > 0) {
			add_impact(0, floorPoint, floorNormal, speed, floorShape);
		}
	}
	return velocity;
}

void GMPCharacter::add_impact(int kind, Vector3 position, Vector3 normal, float speed, b3ShapeId shape) {
	CharacterImpactRecord impact;
	impact.kind = kind;
	impact.position[0] = position.x;
	impact.position[1] = position.y;
	impact.position[2] = position.z;
	impact.normal[0] = normal.x;
	impact.normal[1] = normal.y;
	impact.normal[2] = normal.z;
	impact.speed = speed;
	impact.surfaceType = 0;
	impact.itemId = 0;
	impact.nodeId = 0;
	if (b3Shape_IsValid(shape)) {
		impact.surfaceType = (int64_t)b3Shape_GetSurfaceMaterial(shape).userMaterialId;
		void *userData = b3Body_GetUserData(b3Shape_GetBody(shape));
		GMPPhysicsWorld *physics = GMPPhysicsWorld::get_current();
		if (user_data_is_item(userData)) {
			if (physics != nullptr) {
				impact.itemId = physics->item_id_at_slot(item_slot_from_user_data(userData));
			}
		} else {
			GMPBody *node = node_from_user_data(userData);
			if (node != nullptr) {
				impact.nodeId = node->get_instance_id();
			}
		}
	}
	impacts.push_back(impact);
}

PackedByteArray GMPCharacter::get_impacts() const {
	PackedByteArray bytes;
	bytes.resize((int64_t)(impacts.size() * sizeof(CharacterImpactRecord)));
	if (!impacts.empty()) {
		std::memcpy(bytes.ptrw(), impacts.data(), impacts.size() * sizeof(CharacterImpactRecord));
	}
	return bytes;
}

void GMPCharacter::teleport_character(Transform3D transform) {
	teleport(transform);
	legVelocity = 0;
	onFloor = false;
	againstWall = false;
}

bool GMPCharacter::can_fit_height(float newHeight) const {
	if (!has_body()) {
		return true;
	}
	b3WorldId world = b3Body_GetWorld(get_body_id());
	Vector3 feet = get_global_transform().origin;
	float bottom = stepHeight + radius;
	float top = std::max(bottom, newHeight - radius);
	b3Vec3 points[2] = { { 0, bottom, 0 }, { 0, top, 0 } };
	// A touch thinner, so standing flush against a wall still fits.
	b3ShapeProxy proxy = { points, 2, radius * 0.95f };
	MoverContext context;
	context.self = get_body_id();
	b3World_OverlapShape(world, to_b3(feet), &proxy, mover_query_filter(collisionMask), any_rigid_overlap, &context);
	return !context.hit;
}

bool GMPCharacter::is_on_floor() const {
	return onFloor;
}

Vector3 GMPCharacter::get_floor_normal() const {
	return floorNormal;
}

Vector3 GMPCharacter::get_floor_velocity() const {
	return floorVelocity;
}

// ---- properties ----

void GMPCharacter::set_radius(float value) {
	radius = value;
	make_capsule_shape();
}

float GMPCharacter::get_radius() const {
	return radius;
}

void GMPCharacter::set_height(float value) {
	height = value;
	make_capsule_shape();
}

float GMPCharacter::get_height() const {
	return height;
}

void GMPCharacter::set_step_height(float value) {
	stepHeight = value;
	make_capsule_shape();
}

float GMPCharacter::get_step_height() const {
	return stepHeight;
}

void GMPCharacter::set_max_slope_degrees(float value) {
	maxSlopeDegrees = value;
}

float GMPCharacter::get_max_slope_degrees() const {
	return maxSlopeDegrees;
}

void GMPCharacter::set_ground_snap(float value) {
	groundSnap = value;
}

float GMPCharacter::get_ground_snap() const {
	return groundSnap;
}

void GMPCharacter::set_leg_hertz(float value) {
	legHertz = value;
}

float GMPCharacter::get_leg_hertz() const {
	return legHertz;
}

void GMPCharacter::_bind_methods() {
	ClassDB::bind_method(D_METHOD("move", "velocity", "delta"), &GMPCharacter::move);
	ClassDB::bind_method(D_METHOD("teleport_character", "transform"), &GMPCharacter::teleport_character);
	ClassDB::bind_method(D_METHOD("is_on_floor"), &GMPCharacter::is_on_floor);
	ClassDB::bind_method(D_METHOD("get_floor_normal"), &GMPCharacter::get_floor_normal);
	ClassDB::bind_method(D_METHOD("get_floor_velocity"), &GMPCharacter::get_floor_velocity);
	ClassDB::bind_method(D_METHOD("can_fit_height", "height"), &GMPCharacter::can_fit_height);
	ClassDB::bind_method(D_METHOD("get_impacts"), &GMPCharacter::get_impacts);

	ClassDB::bind_method(D_METHOD("set_radius", "value"), &GMPCharacter::set_radius);
	ClassDB::bind_method(D_METHOD("get_radius"), &GMPCharacter::get_radius);
	ClassDB::bind_method(D_METHOD("set_height", "value"), &GMPCharacter::set_height);
	ClassDB::bind_method(D_METHOD("get_height"), &GMPCharacter::get_height);
	ClassDB::bind_method(D_METHOD("set_step_height", "value"), &GMPCharacter::set_step_height);
	ClassDB::bind_method(D_METHOD("get_step_height"), &GMPCharacter::get_step_height);
	ClassDB::bind_method(D_METHOD("set_max_slope_degrees", "value"), &GMPCharacter::set_max_slope_degrees);
	ClassDB::bind_method(D_METHOD("get_max_slope_degrees"), &GMPCharacter::get_max_slope_degrees);
	ClassDB::bind_method(D_METHOD("set_ground_snap", "value"), &GMPCharacter::set_ground_snap);
	ClassDB::bind_method(D_METHOD("get_ground_snap"), &GMPCharacter::get_ground_snap);
	ClassDB::bind_method(D_METHOD("set_leg_hertz", "value"), &GMPCharacter::set_leg_hertz);
	ClassDB::bind_method(D_METHOD("get_leg_hertz"), &GMPCharacter::get_leg_hertz);

	ADD_GROUP("Character", "");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "radius", PROPERTY_HINT_RANGE, "0.05,2,0.01,suffix:m"), "set_radius", "get_radius");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "height", PROPERTY_HINT_RANGE, "0.3,4,0.01,suffix:m"), "set_height", "get_height");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "step_height", PROPERTY_HINT_RANGE, "0,1,0.01,suffix:m"), "set_step_height", "get_step_height");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "max_slope_degrees", PROPERTY_HINT_RANGE, "0,89,0.1,suffix:°"), "set_max_slope_degrees", "get_max_slope_degrees");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "ground_snap", PROPERTY_HINT_RANGE, "0,1,0.01,suffix:m"), "set_ground_snap", "get_ground_snap");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "leg_hertz", PROPERTY_HINT_RANGE, "1,30,0.1,suffix:Hz"), "set_leg_hertz", "get_leg_hertz");
}
