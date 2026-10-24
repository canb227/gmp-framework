#include "physics_character.h"

#include "math_convert.h"
#include "physics_world.h"

#include <godot_cpp/classes/engine.hpp>
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
// The legs' ring of small casts around the middle one.
const int LegRingProbes = 4;

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
		// It went with the body, and so did the filter joints.
		capsuleShape = b3_nullShapeId;
		ignoredBodies.clear();
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
	if (noclip) {
		// Still there (the body stays, with its id), but touching nothing.
		def.filter.categoryBits = 0;
		def.filter.maskBits = 0;
		def.enableSensorEvents = false;
	}
	// The capsule is no GMPShape, so it has only the character's own tags.
	def.baseMaterial.userMaterialId = tags;
	b3Capsule shape = capsule();
	capsuleShape = b3CreateCapsuleShape(get_body_id(), &def, &shape);
}

// ---- what the mover and the legs collide with ----

struct MoverContext {
	b3BodyId self = b3_nullBodyId;
	// The character moving: what its gravity gun holds is skipped.
	const GMPCharacter *character = nullptr;
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

// Synced bodies (loose items, tools) and dynamic ones give way: the character presses into them while its body
// shoves them. A synced body another peer simulates is kinematic here, but still gives way.
static bool is_soft(b3ShapeId shapeId) {
	if (b3Body_GetType(b3Shape_GetBody(shapeId)) == b3_dynamicBody) {
		return true;
	}
	GMPPhysicsWorld *world = GMPPhysicsWorld::get_current();
	if (world == nullptr) {
		return false;
	}
	Body *body = world->body_of_shape(shapeId);
	return body != nullptr && body->authority != 0;
}

static bool mover_filter(b3ShapeId shapeId, void *context) {
	MoverContext *mover = (MoverContext *)context;
	if (is_self(mover, shapeId) || b3Shape_IsSensor(shapeId)) {
		return false;
	}
	if (mover->character != nullptr && mover->character->ignores_any()) {
		GMPPhysicsWorld *world = GMPPhysicsWorld::get_current();
		if (world != nullptr) {
			Body *body = world->body_of_shape(shapeId);
			if (body != nullptr && mover->character->ignores(body->id)) {
				return false;
			}
		}
	}
	return true;
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
	// Balls cast from the bottom of the capsule straight down past the feet: one a little thinner than the
	// capsule under its middle, and a ring of small ones under its edge, so on a pile of round items the legs
	// stand on the pile's slope rather than on the side of whichever item the middle one lands on.
	float startHeight = stepHeight + radius;
	b3Vec3 centre = { 0, 0, 0 };
	b3QueryFilter filter = mover_query_filter(collisionMask);

	// What they found: the highest ground (the legs stand on it), the normals added up, and the speeds of the
	// soft things under them added up.
	bool found = false;
	float closest = 0;
	b3ShapeId closestShape = b3_nullShapeId;
	b3Pos closestPoint = {};
	Vector3 normalSum;
	Vector3 softVelocitySum;
	int softCount = 0;
	for (int i = 0; i <= LegRingProbes; i++) {
		float ballRadius = radius * 0.9f;
		float offsetX = 0;
		float offsetZ = 0;
		if (i > 0) {
			ballRadius = radius * 0.35f;
			float angle = (i - 1) * 2.0f * B3_PI / LegRingProbes;
			offsetX = std::cos(angle) * radius * 0.6f;
			offsetZ = std::sin(angle) * radius * 0.6f;
		}
		// How far the ball travels before its bottom reaches the feet.
		float restTravel = startHeight - ballRadius;
		float length = restTravel + groundSnap;
		b3Pos start = { feet.x + offsetX, feet.y + startHeight, feet.z + offsetZ };
		b3ShapeProxy ball = { &centre, 1, ballRadius };

		MoverContext context;
		context.self = get_body_id();
		context.character = this;
		b3World_CastShape(world, start, &ball, b3Vec3{ 0, -length, 0 }, filter, legs_callback, &context);
		if (!context.hit) {
			continue;
		}
		float probeDistance = context.hitFraction * length - restTravel;
		if (!found || probeDistance < closest) {
			found = true;
			closest = probeDistance;
			closestShape = context.hitShape;
			closestPoint = context.hitPoint;
		}
		normalSum += to_godot(context.hitNormal);
		if (is_soft(context.hitShape)) {
			b3BodyId body = b3Shape_GetBody(context.hitShape);
			softVelocitySum += to_godot(b3Body_GetWorldPointVelocity(body, context.hitPoint));
			softCount++;
		}
	}

	onFloor = false;
	distance = 0;
	Vector3 normal = Vector3(0, 1, 0);
	if (found && normalSum.length_squared() > 0) {
		normal = normalSum.normalized();
	}
	float minNormalY = std::cos(maxSlopeDegrees * B3_PI / 180.0f);
	if (!found || rising || normal.y < minNormalY) {
		if (!rising && softGraceLeft > 0) {
			// Just off loose items it stood on (between two, on the side of one, the pile shifting under it): it
			// still stands for a moment, on the floor it last had.
			onFloor = true;
			if (found) {
				distance = closest;
			}
			return;
		}
		// Nothing to stand on, too steep to stand on, or jumping.
		softGraceLeft = 0;
		floorSoft = false;
		floorNormal = Vector3(0, 1, 0);
		floorVelocity = Vector3();
		return;
	}
	onFloor = true;
	floorNormal = normal;
	floorPoint = Vector3(closestPoint.x, closestPoint.y, closestPoint.z);
	floorShape = closestShape;
	distance = closest;
	floorSoft = is_soft(closestShape);

	if (floorSoft) {
		// Loose items carry it at their average speed (smoothed in move()).
		softGraceLeft = softGroundGrace;
		floorVelocity = softVelocitySum / (float)softCount;
		return;
	}
	softGraceLeft = 0;
	// Standing still on something moving means moving with it: a body's own motion at that point, plus a
	// belt's surface drive (in the body's axes) while rideBelts is on.
	b3BodyId ground = b3Shape_GetBody(closestShape);
	b3Vec3 pointVelocity = b3Body_GetWorldPointVelocity(ground, closestPoint);
	floorVelocity = to_godot(pointVelocity);
	if (rideBelts) {
		b3SurfaceMaterial material = b3Shape_GetSurfaceMaterial(closestShape);
		b3Vec3 drive = b3RotateVector(b3Body_GetRotation(ground), material.tangentVelocity);
		floorVelocity += to_godot(drive);
	}
}

Vector3 GMPCharacter::smooth_soft_floor_velocity(Vector3 raw, float delta) {
	// Only along the ground: the legs keep the height, and items bouncing up underfoot would otherwise read as
	// a jump (move() lets go of the floor above 0.5 m/s up).
	raw.y = 0;
	// Capped before smoothing, so a fast pile never pulls harder than the cap.
	raw = raw.limit_length(softFloorMaxSpeed);
	float blend = 1.0f - std::exp(-2.0f * B3_PI * softFloorHertz * delta);
	softFloorVelocity = softFloorVelocity.lerp(raw, blend);
	return softFloorVelocity;
}

// ---- moving ----

Vector3 GMPCharacter::move(Vector3 velocity, float delta) {
	Transform3D transform = get_global_transform();
	if (!has_body() || delta <= 0) {
		transform.origin += velocity * delta;
		set_global_transform(transform);
		return velocity;
	}
	if (noclip) {
		// Straight there, through everything.
		transform.origin += velocity * delta;
		set_global_transform(transform);
		return velocity;
	}
	b3WorldId world = b3Body_GetWorld(get_body_id());
	b3Pos feet = to_b3(transform.origin);
	// As the last move left it.
	bool wasOnFloor = onFloor;
	Vector3 velocityIn = velocity;
	softGraceLeft -= delta;

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
	context.character = this;
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
		add_impact(middle - normal * radius, normal, bumpSpeed, context.planeShapes[bumpPlane]);
	}
	againstWall = wall;

	transform.origin = feetAfter;
	set_global_transform(transform);
	// Ready for the next move, and for C# to read in between.
	probe_floor(world, feet, rising, distance);
	if (onFloor && floorSoft) {
		floorVelocity = smooth_soft_floor_velocity(floorVelocity, delta);
	} else {
		// Stepping onto loose items starts from how fast it was carried before.
		softFloorVelocity = floorVelocity;
	}
	if (onFloor && !wasOnFloor) {
		float speed = -velocityIn.dot(floorNormal);
		if (speed > 0) {
			add_impact(floorPoint, floorNormal, speed, floorShape);
		}
	}
	return velocity;
}

void GMPCharacter::add_impact(Vector3 position, Vector3 normal, float speed, b3ShapeId shape) {
	GMPPhysicsWorld *world = GMPPhysicsWorld::get_current();
	if (world == nullptr || speed < MinHitSpeed) {
		return;
	}
	HitRecord hit = {};
	hit.shapeA = NoShapeIndex;
	hit.shapeB = NoShapeIndex;
	// Side A, the character: only its own tags (its capsule is no GMPShape).
	if (sharedId != 0) {
		hit.idA = sharedId;
		hit.tagsA = tags;
	}
	Body *body = world->body_of_shape(shape);
	if (body != nullptr && body->id != 0) {
		hit.idB = body->id;
		hit.shapeB = world->shape_index(shape);
		hit.tagsB = b3Shape_GetSurfaceMaterial(shape).userMaterialId;
	}
	hit.position[0] = position.x;
	hit.position[1] = position.y;
	hit.position[2] = position.z;
	// Hits point from side A to side B: from the character into what it hit.
	hit.normal[0] = -normal.x;
	hit.normal[1] = -normal.y;
	hit.normal[2] = -normal.z;
	hit.speed = speed;
	world->add_character_hit(hit);
}

void GMPCharacter::teleport_character(Transform3D transform) {
	teleport(transform);
	legVelocity = 0;
	onFloor = false;
	againstWall = false;
	floorSoft = false;
	softGraceLeft = 0;
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

// ---- what its gravity gun holds ----

void GMPCharacter::ignore_bodies(PackedInt64Array ids) {
	GMPPhysicsWorld *world = GMPPhysicsWorld::get_current();
	uint64_t frame = Engine::get_singleton()->get_physics_frames();
	uint64_t graceFrames = (uint64_t)(ignoreGrace * Engine::get_singleton()->get_physics_ticks_per_second());
	for (int i = 0; i < ids.size(); i++) {
		uint64_t id = (uint64_t)ids[i];
		bool known = false;
		for (IgnoredBody &ignored : ignoredBodies) {
			if (ignored.id == id) {
				ignored.untilFrame = frame + graceFrames;
				known = true;
			}
		}
		if (known) {
			continue;
		}
		IgnoredBody ignored;
		ignored.id = id;
		ignored.untilFrame = frame + graceFrames;
		// The joint also ends any contact they have now, so an item lifted from under the feet lets go of them.
		if (world != nullptr && sharedId != 0) {
			ignored.joint = world->create_filter_joint((int64_t)id, (int64_t)sharedId);
		}
		ignoredBodies.push_back(ignored);
	}
	// Let go of a while ago: they touch again.
	for (int i = (int)ignoredBodies.size() - 1; i >= 0; i--) {
		if (ignoredBodies[i].untilFrame < frame) {
			if (world != nullptr) {
				world->destroy_joint(ignoredBodies[i].joint);
			}
			ignoredBodies.erase(ignoredBodies.begin() + i);
		}
	}
}

void GMPCharacter::forget_ignored_bodies() {
	GMPPhysicsWorld *world = GMPPhysicsWorld::get_current();
	if (world != nullptr) {
		for (IgnoredBody &ignored : ignoredBodies) {
			world->destroy_joint(ignored.joint);
		}
	}
	ignoredBodies.clear();
}

bool GMPCharacter::ignores(uint64_t id) const {
	for (const IgnoredBody &ignored : ignoredBodies) {
		if (ignored.id == id) {
			return true;
		}
	}
	return false;
}

bool GMPCharacter::ignores_any() const {
	return !ignoredBodies.empty();
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

void GMPCharacter::set_noclip(bool value) {
	noclip = value;
	// Whatever it stood on or ran into no longer counts.
	legVelocity = 0;
	onFloor = false;
	againstWall = false;
	floorVelocity = Vector3();
	floorSoft = false;
	softGraceLeft = 0;
	make_capsule_shape();
}

bool GMPCharacter::get_noclip() const {
	return noclip;
}

void GMPCharacter::set_ride_belts(bool value) {
	rideBelts = value;
}

bool GMPCharacter::get_ride_belts() const {
	return rideBelts;
}

void GMPCharacter::set_soft_ground_grace(float value) {
	softGroundGrace = value;
}

float GMPCharacter::get_soft_ground_grace() const {
	return softGroundGrace;
}

void GMPCharacter::set_soft_floor_hertz(float value) {
	softFloorHertz = value;
}

float GMPCharacter::get_soft_floor_hertz() const {
	return softFloorHertz;
}

void GMPCharacter::set_soft_floor_max_speed(float value) {
	softFloorMaxSpeed = value;
}

float GMPCharacter::get_soft_floor_max_speed() const {
	return softFloorMaxSpeed;
}

void GMPCharacter::set_ignore_grace(float value) {
	ignoreGrace = value;
}

float GMPCharacter::get_ignore_grace() const {
	return ignoreGrace;
}

void GMPCharacter::_bind_methods() {
	ClassDB::bind_method(D_METHOD("move", "velocity", "delta"), &GMPCharacter::move);
	ClassDB::bind_method(D_METHOD("teleport_character", "transform"), &GMPCharacter::teleport_character);
	ClassDB::bind_method(D_METHOD("is_on_floor"), &GMPCharacter::is_on_floor);
	ClassDB::bind_method(D_METHOD("get_floor_normal"), &GMPCharacter::get_floor_normal);
	ClassDB::bind_method(D_METHOD("get_floor_velocity"), &GMPCharacter::get_floor_velocity);
	ClassDB::bind_method(D_METHOD("can_fit_height", "height"), &GMPCharacter::can_fit_height);
	ClassDB::bind_method(D_METHOD("ignore_bodies", "ids"), &GMPCharacter::ignore_bodies);
	ClassDB::bind_method(D_METHOD("forget_ignored_bodies"), &GMPCharacter::forget_ignored_bodies);

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
	ClassDB::bind_method(D_METHOD("set_noclip", "value"), &GMPCharacter::set_noclip);
	ClassDB::bind_method(D_METHOD("get_noclip"), &GMPCharacter::get_noclip);
	ClassDB::bind_method(D_METHOD("set_ride_belts", "value"), &GMPCharacter::set_ride_belts);
	ClassDB::bind_method(D_METHOD("get_ride_belts"), &GMPCharacter::get_ride_belts);
	ClassDB::bind_method(D_METHOD("set_soft_ground_grace", "value"), &GMPCharacter::set_soft_ground_grace);
	ClassDB::bind_method(D_METHOD("get_soft_ground_grace"), &GMPCharacter::get_soft_ground_grace);
	ClassDB::bind_method(D_METHOD("set_soft_floor_hertz", "value"), &GMPCharacter::set_soft_floor_hertz);
	ClassDB::bind_method(D_METHOD("get_soft_floor_hertz"), &GMPCharacter::get_soft_floor_hertz);
	ClassDB::bind_method(D_METHOD("set_soft_floor_max_speed", "value"), &GMPCharacter::set_soft_floor_max_speed);
	ClassDB::bind_method(D_METHOD("get_soft_floor_max_speed"), &GMPCharacter::get_soft_floor_max_speed);
	ClassDB::bind_method(D_METHOD("set_ignore_grace", "value"), &GMPCharacter::set_ignore_grace);
	ClassDB::bind_method(D_METHOD("get_ignore_grace"), &GMPCharacter::get_ignore_grace);

	ADD_GROUP("Character", "");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "radius", PROPERTY_HINT_RANGE, "0.05,2,0.01,suffix:m"), "set_radius", "get_radius");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "height", PROPERTY_HINT_RANGE, "0.3,4,0.01,suffix:m"), "set_height", "get_height");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "step_height", PROPERTY_HINT_RANGE, "0,1,0.01,suffix:m"), "set_step_height", "get_step_height");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "max_slope_degrees", PROPERTY_HINT_RANGE, "0,89,0.1,suffix:°"), "set_max_slope_degrees", "get_max_slope_degrees");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "ground_snap", PROPERTY_HINT_RANGE, "0,1,0.01,suffix:m"), "set_ground_snap", "get_ground_snap");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "leg_hertz", PROPERTY_HINT_RANGE, "1,30,0.1,suffix:Hz"), "set_leg_hertz", "get_leg_hertz");
	ADD_PROPERTY(PropertyInfo(Variant::BOOL, "ride_belts"), "set_ride_belts", "get_ride_belts");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "soft_ground_grace", PROPERTY_HINT_RANGE, "0,1,0.01,suffix:s"), "set_soft_ground_grace", "get_soft_ground_grace");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "soft_floor_hertz", PROPERTY_HINT_RANGE, "0.1,30,0.1,suffix:Hz"), "set_soft_floor_hertz", "get_soft_floor_hertz");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "soft_floor_max_speed", PROPERTY_HINT_RANGE, "0,10,0.01,suffix:m/s"), "set_soft_floor_max_speed", "get_soft_floor_max_speed");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "ignore_grace", PROPERTY_HINT_RANGE, "0,2,0.01,suffix:s"), "set_ignore_grace", "get_ignore_grace");
	// Set at run time only (not saved in scenes).
	ADD_PROPERTY(PropertyInfo(Variant::BOOL, "noclip", PROPERTY_HINT_NONE, "", PROPERTY_USAGE_NONE), "set_noclip", "get_noclip");
}
