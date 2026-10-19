#include "physics_shape.h"

#include "body_user_data.h"
#include "math_convert.h"
#include "physics_body.h"
#include "physics_world.h"

#include <godot_cpp/core/class_db.hpp>

#include <algorithm>
#include <vector>

using namespace godot;

GMPShape::GMPShape() {
}

GMPShape::~GMPShape() {
	detach();
}

void GMPShape::_notification(int what) {
	if (what == NOTIFICATION_ENTER_TREE) {
		// Added under a body that already exists (a body that isn't made yet attaches its shapes itself).
		GMPBody *body = find_body();
		if (body != nullptr && body->has_body()) {
			attach(body);
		}
	}
	if (what == NOTIFICATION_EXIT_TREE) {
		detach();
	}
}

GMPBody *GMPShape::find_body() const {
	Node *node = get_parent();
	while (node != nullptr) {
		GMPBody *body = Object::cast_to<GMPBody>(node);
		if (body != nullptr) {
			return body;
		}
		node = node->get_parent();
	}
	return nullptr;
}

void GMPShape::attach(GMPBody *body) {
	detach();
	if (body == nullptr || !body->has_body() || !is_inside_tree()) {
		return;
	}
	b3BodyId bodyId = body->get_body_id();

	// Where this shape sits in the body, and its scale there.
	Transform3D local = body->get_global_transform().affine_inverse() * get_global_transform();
	Vector3 scale = local.basis.get_scale();
	Quaternion rotation = local.basis.get_rotation_quaternion();
	b3Transform placement = { to_b3(local.origin), to_b3(rotation) };
	// A sphere or capsule can't be stretched: it takes the largest scale.
	float largestScale = std::max(scale.x, std::max(scale.y, scale.z));

	b3ShapeDef def = b3DefaultShapeDef();
	def.density = density;
	def.baseMaterial.friction = friction;
	def.baseMaterial.restitution = restitution;
	def.baseMaterial.rollingResistance = rollingResistance;
	// Box3D wants it in the body's axes.
	def.baseMaterial.tangentVelocity = to_b3(rotation.xform(tangentVelocity));
	def.baseMaterial.userMaterialId = (uint64_t)surfaceType;
	def.isSensor = isSensor;
	// Sensors only see shapes that have this on, so every shape has it, and a sensor needs it to report.
	def.enableSensorEvents = true;
	// Hits are read from begin touch events too (see read_contact_events).
	def.enableContactEvents = body->contactMonitor || body->hitEvents;
	def.enableHitEvents = body->hitEvents;
	def.filter.categoryBits = body->collisionLayer;
	def.filter.maskBits = body->collisionMask;
	def.userData = this;

	if (shape == BOX) {
		Vector3 half = size * scale / 2;
		b3BoxHull box = b3MakeTransformedBoxHull(half.x, half.y, half.z, placement);
		shapeId = b3CreateHullShape(bodyId, &def, &box.base);
	} else if (shape == SPHERE) {
		b3Sphere sphere = { to_b3(local.origin), radius * largestScale };
		shapeId = b3CreateSphereShape(bodyId, &def, &sphere);
	} else if (shape == CAPSULE) {
		float capsuleRadius = radius * largestScale;
		// From the centre to each hemisphere's centre, along the shape's Y axis.
		float halfSegment = std::max(0.0f, height * scale.y / 2 - capsuleRadius);
		Vector3 axis = rotation.xform(Vector3(0, 1, 0));
		b3Capsule capsule = { to_b3(local.origin - axis * halfSegment), to_b3(local.origin + axis * halfSegment), capsuleRadius };
		shapeId = b3CreateCapsuleShape(bodyId, &def, &capsule);
	} else if (shape == CYLINDER) {
		b3HullData *cylinder = b3CreateCylinder(height, radius, -height / 2, std::clamp(sides, 3, 32));
		shapeId = b3CreateTransformedHullShape(bodyId, &def, cylinder, placement, to_b3(scale));
		// Box3D keeps its own copy.
		b3DestroyHull(cylinder);
	} else if (shape == HULL && mesh.is_valid()) {
		PackedVector3Array faces = mesh->get_faces();
		std::vector<b3Vec3> points;
		for (int i = 0; i < faces.size(); i++) {
			points.push_back(to_b3(faces[i]));
		}
		b3HullData *hull = nullptr;
		if (!points.empty()) {
			hull = b3CreateHull(points.data(), (int)points.size(), B3_MAX_HULL_VERTICES);
		}
		if (hull == nullptr) {
			ERR_PRINT(vformat("%s: couldn't make a hull from its mesh.", get_path()));
			return;
		}
		shapeId = b3CreateTransformedHullShape(bodyId, &def, hull, placement, to_b3(scale));
		b3DestroyHull(hull);
	} else if (shape == MESH && mesh.is_valid()) {
		// Box3D meshes have no placement of their own, so the triangles are moved into the body's space.
		PackedVector3Array faces = mesh->get_faces();
		std::vector<b3Vec3> vertices;
		std::vector<int32_t> indices;
		for (int i = 0; i < faces.size(); i++) {
			vertices.push_back(to_b3(local.xform(faces[i])));
			indices.push_back(i);
		}
		if (vertices.size() < 3) {
			return;
		}
		b3MeshDef meshDef = {};
		meshDef.vertices = vertices.data();
		meshDef.indices = indices.data();
		meshDef.vertexCount = (int)vertices.size();
		meshDef.triangleCount = (int)vertices.size() / 3;
		meshDef.weldVertices = true;
		meshDef.weldTolerance = 0.001f;
		meshDef.identifyEdges = true;
		// Godot's front faces wind clockwise.
		meshDef.clockWiseWinding = true;
		meshData = b3CreateMesh(&meshDef, nullptr, 0);
		if (meshData == nullptr) {
			ERR_PRINT(vformat("%s: couldn't make a collision mesh.", get_path()));
			return;
		}
		shapeId = b3CreateMeshShape(bodyId, &def, meshData, b3Vec3{ 1, 1, 1 });
	}

	if (b3Shape_IsValid(shapeId)) {
		attachedBody = body;
	}
}

void GMPShape::detach() {
	// The shape is gone already if its whole world went.
	if (b3Shape_IsValid(shapeId)) {
		b3DestroyShape(shapeId, true);
	}
	shapeId = b3_nullShapeId;
	attachedBody = nullptr;
	if (meshData != nullptr) {
		b3DestroyMesh(meshData);
		meshData = nullptr;
	}
}

void GMPShape::rebuild() {
	if (attachedBody != nullptr) {
		attach(attachedBody);
	}
}

b3ShapeId GMPShape::get_shape_id() const {
	return shapeId;
}

PackedInt64Array GMPShape::get_overlapping_items() const {
	PackedInt64Array result;
	if (!isSensor || !b3Shape_IsValid(shapeId)) {
		return result;
	}
	GMPPhysicsWorld *world = GMPPhysicsWorld::get_current();
	int capacity = b3Shape_GetSensorCapacity(shapeId);
	std::vector<b3ShapeId> visitors(capacity);
	int count = b3Shape_GetSensorData(shapeId, visitors.data(), capacity);
	for (int i = 0; i < count; i++) {
		if (!b3Shape_IsValid(visitors[i])) {
			continue;
		}
		void *userData = b3Body_GetUserData(b3Shape_GetBody(visitors[i]));
		if (world != nullptr && user_data_is_item(userData)) {
			uint64_t id = world->item_id_at_slot(item_slot_from_user_data(userData));
			if (id != 0) {
				result.push_back((int64_t)id);
			}
		}
	}
	return result;
}

Array GMPShape::get_overlapping_bodies() const {
	Array result;
	if (!isSensor || !b3Shape_IsValid(shapeId)) {
		return result;
	}
	int capacity = b3Shape_GetSensorCapacity(shapeId);
	std::vector<b3ShapeId> visitors(capacity);
	int count = b3Shape_GetSensorData(shapeId, visitors.data(), capacity);
	for (int i = 0; i < count; i++) {
		if (!b3Shape_IsValid(visitors[i])) {
			continue;
		}
		GMPBody *body = node_from_user_data(b3Body_GetUserData(b3Shape_GetBody(visitors[i])));
		// A body with several shapes inside is listed once.
		if (body != nullptr && !result.has(body)) {
			result.push_back(body);
		}
	}
	return result;
}

// ---- properties: each change remakes the Box3D shape ----

void GMPShape::set_shape(int value) {
	shape = (ShapeType)value;
	rebuild();
}

int GMPShape::get_shape() const {
	return shape;
}

void GMPShape::set_size(Vector3 value) {
	size = value;
	rebuild();
}

Vector3 GMPShape::get_size() const {
	return size;
}

void GMPShape::set_radius(float value) {
	radius = value;
	rebuild();
}

float GMPShape::get_radius() const {
	return radius;
}

void GMPShape::set_height(float value) {
	height = value;
	rebuild();
}

float GMPShape::get_height() const {
	return height;
}

void GMPShape::set_sides(int value) {
	sides = value;
	rebuild();
}

int GMPShape::get_sides() const {
	return sides;
}

void GMPShape::set_mesh(const Ref<Mesh> &value) {
	mesh = value;
	rebuild();
}

Ref<Mesh> GMPShape::get_mesh() const {
	return mesh;
}

void GMPShape::set_density(float value) {
	density = value;
	rebuild();
}

float GMPShape::get_density() const {
	return density;
}

void GMPShape::set_friction(float value) {
	friction = value;
	rebuild();
}

float GMPShape::get_friction() const {
	return friction;
}

void GMPShape::set_restitution(float value) {
	restitution = value;
	rebuild();
}

float GMPShape::get_restitution() const {
	return restitution;
}

void GMPShape::set_rolling_resistance(float value) {
	rollingResistance = value;
	rebuild();
}

float GMPShape::get_rolling_resistance() const {
	return rollingResistance;
}

void GMPShape::set_tangent_velocity(Vector3 value) {
	tangentVelocity = value;
	rebuild();
}

Vector3 GMPShape::get_tangent_velocity() const {
	return tangentVelocity;
}

void GMPShape::set_surface_type(int64_t value) {
	surfaceType = value;
	rebuild();
}

int64_t GMPShape::get_surface_type() const {
	return surfaceType;
}

void GMPShape::set_is_sensor(bool value) {
	isSensor = value;
	rebuild();
}

bool GMPShape::get_is_sensor() const {
	return isSensor;
}

void GMPShape::_bind_methods() {
	BIND_ENUM_CONSTANT(BOX);
	BIND_ENUM_CONSTANT(SPHERE);
	BIND_ENUM_CONSTANT(CAPSULE);
	BIND_ENUM_CONSTANT(CYLINDER);
	BIND_ENUM_CONSTANT(HULL);
	BIND_ENUM_CONSTANT(MESH);

	ClassDB::bind_method(D_METHOD("get_overlapping_items"), &GMPShape::get_overlapping_items);
	ClassDB::bind_method(D_METHOD("get_overlapping_bodies"), &GMPShape::get_overlapping_bodies);

	ClassDB::bind_method(D_METHOD("set_shape", "value"), &GMPShape::set_shape);
	ClassDB::bind_method(D_METHOD("get_shape"), &GMPShape::get_shape);
	ClassDB::bind_method(D_METHOD("set_size", "value"), &GMPShape::set_size);
	ClassDB::bind_method(D_METHOD("get_size"), &GMPShape::get_size);
	ClassDB::bind_method(D_METHOD("set_radius", "value"), &GMPShape::set_radius);
	ClassDB::bind_method(D_METHOD("get_radius"), &GMPShape::get_radius);
	ClassDB::bind_method(D_METHOD("set_height", "value"), &GMPShape::set_height);
	ClassDB::bind_method(D_METHOD("get_height"), &GMPShape::get_height);
	ClassDB::bind_method(D_METHOD("set_sides", "value"), &GMPShape::set_sides);
	ClassDB::bind_method(D_METHOD("get_sides"), &GMPShape::get_sides);
	ClassDB::bind_method(D_METHOD("set_mesh", "value"), &GMPShape::set_mesh);
	ClassDB::bind_method(D_METHOD("get_mesh"), &GMPShape::get_mesh);
	ClassDB::bind_method(D_METHOD("set_density", "value"), &GMPShape::set_density);
	ClassDB::bind_method(D_METHOD("get_density"), &GMPShape::get_density);
	ClassDB::bind_method(D_METHOD("set_friction", "value"), &GMPShape::set_friction);
	ClassDB::bind_method(D_METHOD("get_friction"), &GMPShape::get_friction);
	ClassDB::bind_method(D_METHOD("set_restitution", "value"), &GMPShape::set_restitution);
	ClassDB::bind_method(D_METHOD("get_restitution"), &GMPShape::get_restitution);
	ClassDB::bind_method(D_METHOD("set_rolling_resistance", "value"), &GMPShape::set_rolling_resistance);
	ClassDB::bind_method(D_METHOD("get_rolling_resistance"), &GMPShape::get_rolling_resistance);
	ClassDB::bind_method(D_METHOD("set_tangent_velocity", "value"), &GMPShape::set_tangent_velocity);
	ClassDB::bind_method(D_METHOD("get_tangent_velocity"), &GMPShape::get_tangent_velocity);
	ClassDB::bind_method(D_METHOD("set_surface_type", "value"), &GMPShape::set_surface_type);
	ClassDB::bind_method(D_METHOD("get_surface_type"), &GMPShape::get_surface_type);
	ClassDB::bind_method(D_METHOD("set_is_sensor", "value"), &GMPShape::set_is_sensor);
	ClassDB::bind_method(D_METHOD("get_is_sensor"), &GMPShape::get_is_sensor);

	ADD_GROUP("Shape", "");
	ADD_PROPERTY(PropertyInfo(Variant::INT, "shape", PROPERTY_HINT_ENUM, "Box,Sphere,Capsule,Cylinder,Hull,Mesh"), "set_shape", "get_shape");
	ADD_PROPERTY(PropertyInfo(Variant::VECTOR3, "size", PROPERTY_HINT_NONE, "suffix:m"), "set_size", "get_size");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "radius", PROPERTY_HINT_RANGE, "0.001,100,0.001,or_greater,suffix:m"), "set_radius", "get_radius");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "height", PROPERTY_HINT_RANGE, "0.001,100,0.001,or_greater,suffix:m"), "set_height", "get_height");
	ADD_PROPERTY(PropertyInfo(Variant::INT, "sides", PROPERTY_HINT_RANGE, "3,32"), "set_sides", "get_sides");
	ADD_PROPERTY(PropertyInfo(Variant::OBJECT, "mesh", PROPERTY_HINT_RESOURCE_TYPE, "Mesh"), "set_mesh", "get_mesh");
	ADD_GROUP("Surface", "");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "density", PROPERTY_HINT_RANGE, "0,20000,0.1,or_greater,suffix:kg/m³"), "set_density", "get_density");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "friction", PROPERTY_HINT_RANGE, "0,2,0.01,or_greater"), "set_friction", "get_friction");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "restitution", PROPERTY_HINT_RANGE, "0,1,0.01"), "set_restitution", "get_restitution");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "rolling_resistance", PROPERTY_HINT_RANGE, "0,1,0.01,or_greater"), "set_rolling_resistance", "get_rolling_resistance");
	ADD_PROPERTY(PropertyInfo(Variant::VECTOR3, "tangent_velocity", PROPERTY_HINT_NONE, "suffix:m/s"), "set_tangent_velocity", "get_tangent_velocity");
	ADD_PROPERTY(PropertyInfo(Variant::INT, "surface_type"), "set_surface_type", "get_surface_type");
	ADD_PROPERTY(PropertyInfo(Variant::BOOL, "is_sensor"), "set_is_sensor", "get_is_sensor");

	ADD_SIGNAL(MethodInfo("item_entered", PropertyInfo(Variant::INT, "item_id")));
	ADD_SIGNAL(MethodInfo("item_exited", PropertyInfo(Variant::INT, "item_id")));
	ADD_SIGNAL(MethodInfo("body_entered", PropertyInfo(Variant::OBJECT, "body", PROPERTY_HINT_NODE_TYPE, "GMPBody")));
	ADD_SIGNAL(MethodInfo("body_exited", PropertyInfo(Variant::OBJECT, "body", PROPERTY_HINT_NODE_TYPE, "GMPBody")));
}
