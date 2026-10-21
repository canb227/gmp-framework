#include "physics_shape.h"

#include "math_convert.h"
#include "physics_body.h"
#include "physics_world.h"
#include "tags.h"

#include <godot_cpp/classes/array_mesh.hpp>
#include <godot_cpp/classes/engine.hpp>
#include <godot_cpp/classes/project_settings.hpp>
#include <godot_cpp/classes/scene_tree.hpp>
#include <godot_cpp/classes/standard_material3d.hpp>
#include <godot_cpp/core/class_db.hpp>
#include <godot_cpp/core/math.hpp>

#include <algorithm>
#include <cmath>
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
		update_debug_drawing();
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
	Quaternion rotation = local.basis.get_rotation_quaternion();

	b3ShapeDef def = b3DefaultShapeDef();
	def.density = density;
	def.baseMaterial.friction = friction;
	def.baseMaterial.restitution = restitution;
	def.baseMaterial.rollingResistance = rollingResistance;
	// Box3D wants it in the body's axes.
	def.baseMaterial.tangentVelocity = to_b3(rotation.xform(tangentVelocity));
	def.baseMaterial.userMaterialId = tags | body->tags;
	def.isSensor = isSensor;
	// Sensors only see shapes that have this on, so every shape has it, and a sensor needs it to report.
	def.enableSensorEvents = true;
	// No contact begin/end events (they cost per contact), but every impact makes a hit (for sounds and effects).
	def.enableContactEvents = false;
	def.enableHitEvents = true;
	def.filter.categoryBits = body->collisionLayer;
	def.filter.maskBits = body->collisionMask;
	def.userData = this;

	if (shape == MESH) {
		if (mesh.is_null()) {
			return;
		}
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
	} else {
		ShapeGeometry geometry;
		geometry.type = shape;
		geometry.size = size;
		geometry.radius = radius;
		geometry.height = height;
		geometry.sides = sides;
		geometry.maxHullVertices = maxHullVertices;
		if (shape == HULL) {
			if (mesh.is_null()) {
				return;
			}
			geometry.hullPoints = mesh->get_faces();
		}
		PlacedShape placed;
		if (!place_shape(geometry, local, placed)) {
			ERR_PRINT(vformat("%s: couldn't make a collider from its settings or mesh.", get_path()));
			return;
		}
		shapeId = create_placed_shape(bodyId, def, placed);
		free_placed_shape(placed);
	}

	if (b3Shape_IsValid(shapeId)) {
		attachedBody = body;
	}
}

bool place_shape(const ShapeGeometry &geometry, const Transform3D &local, PlacedShape &placed) {
	Vector3 scale = local.basis.get_scale();
	Quaternion rotation = local.basis.get_rotation_quaternion();
	// A sphere or capsule can't be stretched: it takes the largest scale.
	float largestScale = std::max(scale.x, std::max(scale.y, scale.z));
	placed.type = geometry.type;

	if (geometry.type == GMPShape::BOX) {
		Vector3 half = geometry.size * scale / 2;
		b3Transform placement = { to_b3(local.origin), to_b3(rotation) };
		placed.box = b3MakeTransformedBoxHull(half.x, half.y, half.z, placement);
		return true;
	}
	if (geometry.type == GMPShape::SPHERE) {
		placed.sphere = { to_b3(local.origin), geometry.radius * largestScale };
		return true;
	}
	if (geometry.type == GMPShape::CAPSULE) {
		float capsuleRadius = geometry.radius * largestScale;
		// From the centre to each hemisphere's centre, along the shape's Y axis.
		float halfSegment = std::max(0.0f, geometry.height * scale.y / 2 - capsuleRadius);
		Vector3 axis = rotation.xform(Vector3(0, 1, 0));
		placed.capsule = { to_b3(local.origin - axis * halfSegment), to_b3(local.origin + axis * halfSegment), capsuleRadius };
		return true;
	}

	// Cylinders and hulls: a hull around the points, each moved into the body first.
	std::vector<b3Vec3> points;
	int maxVertices = geometry.maxHullVertices;
	if (geometry.type == GMPShape::CYLINDER) {
		b3HullData *cylinder = b3CreateCylinder(geometry.height, geometry.radius, -geometry.height / 2, std::clamp(geometry.sides, 3, 32));
		if (cylinder == nullptr) {
			return false;
		}
		const b3Vec3 *corners = b3GetHullPoints(cylinder);
		for (int i = 0; i < cylinder->vertexCount; i++) {
			points.push_back(to_b3(local.xform(to_godot(corners[i]))));
		}
		b3DestroyHull(cylinder);
		maxVertices = B3_MAX_HULL_VERTICES;
	} else if (geometry.type == GMPShape::HULL) {
		for (int i = 0; i < geometry.hullPoints.size(); i++) {
			points.push_back(to_b3(local.xform(geometry.hullPoints[i])));
		}
	} else {
		return false;
	}
	if (points.size() < 4) {
		return false;
	}
	placed.hull = b3CreateHull(points.data(), (int)points.size(), maxVertices);
	return placed.hull != nullptr;
}

b3ShapeId create_placed_shape(b3BodyId bodyId, const b3ShapeDef &def, const PlacedShape &placed) {
	if (placed.type == GMPShape::BOX) {
		return b3CreateHullShape(bodyId, &def, &placed.box.base);
	}
	if (placed.type == GMPShape::SPHERE) {
		return b3CreateSphereShape(bodyId, &def, &placed.sphere);
	}
	if (placed.type == GMPShape::CAPSULE) {
		return b3CreateCapsuleShape(bodyId, &def, &placed.capsule);
	}
	if (placed.hull == nullptr) {
		return b3_nullShapeId;
	}
	return b3CreateHullShape(bodyId, &def, placed.hull);
}

void free_placed_shape(PlacedShape &placed) {
	if (placed.hull != nullptr) {
		b3DestroyHull(placed.hull);
		placed.hull = nullptr;
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
	update_debug_drawing();
}

// ---- seeing it ----

// Line segments around part of a circle: center plus radius times (axisA cos + axisB sin), from one angle to another.
static void add_arc(PackedVector3Array &lines, Vector3 center, Vector3 axisA, Vector3 axisB, float radius, float fromAngle, float toAngle, int segments) {
	for (int i = 0; i < segments; i++) {
		float angle0 = fromAngle + (toAngle - fromAngle) * i / segments;
		float angle1 = fromAngle + (toAngle - fromAngle) * (i + 1) / segments;
		lines.push_back(center + (axisA * std::cos(angle0) + axisB * std::sin(angle0)) * radius);
		lines.push_back(center + (axisA * std::cos(angle1) + axisB * std::sin(angle1)) * radius);
	}
}

// Every edge of a hull once (its half edges come in twin pairs, side by side).
static void add_hull_lines(PackedVector3Array &lines, const b3HullData *hull) {
	const b3Vec3 *points = b3GetHullPoints(hull);
	const b3HullHalfEdge *edges = b3GetHullEdges(hull);
	for (int i = 0; i < hull->edgeCount; i += 2) {
		lines.push_back(to_godot(points[edges[i].origin]));
		lines.push_back(to_godot(points[edges[edges[i].twin].origin]));
	}
}

PackedVector3Array GMPShape::get_debug_lines() const {
	PackedVector3Array lines;
	if (shape == BOX) {
		// The 12 edges: from each corner to the corners one bit (one axis) away from it.
		Vector3 half = size / 2;
		for (int corner = 0; corner < 8; corner++) {
			for (int bit = 1; bit < 8; bit *= 2) {
				if ((corner & bit) != 0) {
					continue;
				}
				int other = corner | bit;
				lines.push_back(Vector3((corner & 1) ? half.x : -half.x, (corner & 2) ? half.y : -half.y, (corner & 4) ? half.z : -half.z));
				lines.push_back(Vector3((other & 1) ? half.x : -half.x, (other & 2) ? half.y : -half.y, (other & 4) ? half.z : -half.z));
			}
		}
	} else if (shape == SPHERE) {
		add_arc(lines, Vector3(), Vector3(1, 0, 0), Vector3(0, 1, 0), radius, 0, Math::TAU, 32);
		add_arc(lines, Vector3(), Vector3(0, 1, 0), Vector3(0, 0, 1), radius, 0, Math::TAU, 32);
		add_arc(lines, Vector3(), Vector3(0, 0, 1), Vector3(1, 0, 0), radius, 0, Math::TAU, 32);
	} else if (shape == CAPSULE) {
		// Rings where the sides meet the ends, the four side lines, and the two end caps' arcs.
		float halfSegment = std::max(0.0f, height / 2 - radius);
		Vector3 top = Vector3(0, halfSegment, 0);
		Vector3 bottom = Vector3(0, -halfSegment, 0);
		add_arc(lines, top, Vector3(1, 0, 0), Vector3(0, 0, 1), radius, 0, Math::TAU, 32);
		add_arc(lines, bottom, Vector3(1, 0, 0), Vector3(0, 0, 1), radius, 0, Math::TAU, 32);
		Vector3 sideOffsets[4] = { Vector3(radius, 0, 0), Vector3(-radius, 0, 0), Vector3(0, 0, radius), Vector3(0, 0, -radius) };
		for (const Vector3 &side : sideOffsets) {
			lines.push_back(top + side);
			lines.push_back(bottom + side);
		}
		add_arc(lines, top, Vector3(1, 0, 0), Vector3(0, 1, 0), radius, 0, Math::PI, 16);
		add_arc(lines, top, Vector3(0, 0, 1), Vector3(0, 1, 0), radius, 0, Math::PI, 16);
		add_arc(lines, bottom, Vector3(1, 0, 0), Vector3(0, 1, 0), radius, Math::PI, Math::TAU, 16);
		add_arc(lines, bottom, Vector3(0, 0, 1), Vector3(0, 1, 0), radius, Math::PI, Math::TAU, 16);
	} else if (shape == CYLINDER || shape == HULL) {
		// The hull exactly as the collider has it.
		ShapeGeometry geometry;
		geometry.type = shape;
		geometry.radius = radius;
		geometry.height = height;
		geometry.sides = sides;
		geometry.maxHullVertices = maxHullVertices;
		if (shape == HULL) {
			if (mesh.is_null()) {
				return lines;
			}
			geometry.hullPoints = mesh->get_faces();
		}
		PlacedShape placed;
		if (place_shape(geometry, Transform3D(), placed)) {
			add_hull_lines(lines, placed.hull);
		}
		free_placed_shape(placed);
	} else if (shape == MESH && mesh.is_valid()) {
		PackedVector3Array faces = mesh->get_faces();
		for (int i = 0; i + 2 < faces.size(); i += 3) {
			lines.push_back(faces[i]);
			lines.push_back(faces[i + 1]);
			lines.push_back(faces[i + 1]);
			lines.push_back(faces[i + 2]);
			lines.push_back(faces[i + 2]);
			lines.push_back(faces[i]);
		}
	}
	return lines;
}

void GMPShape::update_debug_drawing() {
	if (!is_inside_tree()) {
		return;
	}
	if (Engine::get_singleton()->is_editor_hint()) {
		update_gizmos();
		return;
	}
	bool show = debugVisible && get_tree()->is_debugging_collisions_hint();
	if (!show) {
		if (debugMesh != nullptr) {
			debugMesh->queue_free();
			debugMesh = nullptr;
		}
		return;
	}
	Ref<ArrayMesh> lineMesh;
	lineMesh.instantiate();
	PackedVector3Array lines = get_debug_lines();
	if (!lines.is_empty()) {
		Array arrays;
		arrays.resize(Mesh::ARRAY_MAX);
		arrays[Mesh::ARRAY_VERTEX] = lines;
		lineMesh->add_surface_from_arrays(Mesh::PRIMITIVE_LINES, arrays);
	}
	// Godot's own colour for collision shapes; sensors in their own colour.
	Color color = ProjectSettings::get_singleton()->get_setting("debug/shapes/collision/shape_color", Color(0, 0.6, 0.7, 0.42));
	if (isSensor) {
		color = Color(1.0, 0.75, 0.2, color.a);
	}
	Ref<StandardMaterial3D> material;
	material.instantiate();
	material->set_shading_mode(BaseMaterial3D::SHADING_MODE_UNSHADED);
	material->set_transparency(BaseMaterial3D::TRANSPARENCY_ALPHA);
	material->set_albedo(color);
	if (debugMesh == nullptr) {
		debugMesh = memnew(MeshInstance3D);
		debugMesh->set_cast_shadows_setting(GeometryInstance3D::SHADOW_CASTING_SETTING_OFF);
		add_child(debugMesh, false, INTERNAL_MODE_FRONT);
	}
	debugMesh->set_mesh(lineMesh);
	debugMesh->set_material_override(material);
}

b3ShapeId GMPShape::get_shape_id() const {
	return shapeId;
}

PackedInt64Array GMPShape::get_overlapping() const {
	PackedInt64Array result;
	GMPPhysicsWorld *world = GMPPhysicsWorld::get_current();
	if (!isSensor || !b3Shape_IsValid(shapeId) || world == nullptr) {
		return result;
	}
	int capacity = b3Shape_GetSensorCapacity(shapeId);
	std::vector<b3ShapeId> visitors(capacity);
	int count = b3Shape_GetSensorData(shapeId, visitors.data(), capacity);
	for (int i = 0; i < count; i++) {
		Body *body = world->body_of_shape(visitors[i]);
		// A body with several shapes inside is listed once.
		if (body != nullptr && body->id != 0 && !result.has((int64_t)body->id)) {
			result.push_back((int64_t)body->id);
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

void GMPShape::set_max_hull_vertices(int value) {
	maxHullVertices = std::clamp(value, 4, B3_MAX_HULL_VERTICES);
	rebuild();
}

int GMPShape::get_max_hull_vertices() const {
	return maxHullVertices;
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

void GMPShape::set_tags(int64_t value) {
	tags = (uint64_t)value;
	rebuild();
}

int64_t GMPShape::get_tags() const {
	return (int64_t)tags;
}

void GMPShape::set_is_sensor(bool value) {
	isSensor = value;
	rebuild();
}

bool GMPShape::get_is_sensor() const {
	return isSensor;
}

void GMPShape::set_debug_visible(bool value) {
	debugVisible = value;
	update_debug_drawing();
}

bool GMPShape::get_debug_visible() const {
	return debugVisible;
}

void GMPShape::_bind_methods() {
	BIND_ENUM_CONSTANT(BOX);
	BIND_ENUM_CONSTANT(SPHERE);
	BIND_ENUM_CONSTANT(CAPSULE);
	BIND_ENUM_CONSTANT(CYLINDER);
	BIND_ENUM_CONSTANT(HULL);
	BIND_ENUM_CONSTANT(MESH);

	ClassDB::bind_method(D_METHOD("get_overlapping"), &GMPShape::get_overlapping);

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
	ClassDB::bind_method(D_METHOD("set_max_hull_vertices", "value"), &GMPShape::set_max_hull_vertices);
	ClassDB::bind_method(D_METHOD("get_max_hull_vertices"), &GMPShape::get_max_hull_vertices);
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
	ClassDB::bind_method(D_METHOD("set_tags", "value"), &GMPShape::set_tags);
	ClassDB::bind_method(D_METHOD("get_tags"), &GMPShape::get_tags);
	ClassDB::bind_method(D_METHOD("set_is_sensor", "value"), &GMPShape::set_is_sensor);
	ClassDB::bind_method(D_METHOD("get_is_sensor"), &GMPShape::get_is_sensor);
	ClassDB::bind_method(D_METHOD("set_debug_visible", "value"), &GMPShape::set_debug_visible);
	ClassDB::bind_method(D_METHOD("get_debug_visible"), &GMPShape::get_debug_visible);
	ClassDB::bind_method(D_METHOD("get_debug_lines"), &GMPShape::get_debug_lines);

	ADD_GROUP("Shape", "");
	ADD_PROPERTY(PropertyInfo(Variant::INT, "shape", PROPERTY_HINT_ENUM, "Box,Sphere,Capsule,Cylinder,Hull,Mesh"), "set_shape", "get_shape");
	ADD_PROPERTY(PropertyInfo(Variant::VECTOR3, "size", PROPERTY_HINT_NONE, "suffix:m"), "set_size", "get_size");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "radius", PROPERTY_HINT_RANGE, "0.001,100,0.001,or_greater,suffix:m"), "set_radius", "get_radius");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "height", PROPERTY_HINT_RANGE, "0.001,100,0.001,or_greater,suffix:m"), "set_height", "get_height");
	ADD_PROPERTY(PropertyInfo(Variant::INT, "sides", PROPERTY_HINT_RANGE, "3,32"), "set_sides", "get_sides");
	ADD_PROPERTY(PropertyInfo(Variant::OBJECT, "mesh", PROPERTY_HINT_RESOURCE_TYPE, "Mesh"), "set_mesh", "get_mesh");
	ADD_PROPERTY(PropertyInfo(Variant::INT, "max_hull_vertices", PROPERTY_HINT_RANGE, "4,128"), "set_max_hull_vertices", "get_max_hull_vertices");
	ADD_GROUP("Surface", "");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "density", PROPERTY_HINT_RANGE, "0,20000,0.1,or_greater,suffix:kg/m³"), "set_density", "get_density");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "friction", PROPERTY_HINT_RANGE, "0,2,0.01,or_greater"), "set_friction", "get_friction");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "restitution", PROPERTY_HINT_RANGE, "0,1,0.01"), "set_restitution", "get_restitution");
	ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "rolling_resistance", PROPERTY_HINT_RANGE, "0,1,0.01,or_greater"), "set_rolling_resistance", "get_rolling_resistance");
	ADD_PROPERTY(PropertyInfo(Variant::VECTOR3, "tangent_velocity", PROPERTY_HINT_NONE, "suffix:m/s"), "set_tangent_velocity", "get_tangent_velocity");
	ADD_PROPERTY(PropertyInfo(Variant::INT, "tags", PROPERTY_HINT_FLAGS, TagHintString), "set_tags", "get_tags");
	ADD_PROPERTY(PropertyInfo(Variant::BOOL, "is_sensor"), "set_is_sensor", "get_is_sensor");
	ADD_GROUP("Debug", "");
	ADD_PROPERTY(PropertyInfo(Variant::BOOL, "debug_visible"), "set_debug_visible", "get_debug_visible");

	ADD_SIGNAL(MethodInfo("entered", PropertyInfo(Variant::INT, "id")));
	ADD_SIGNAL(MethodInfo("exited", PropertyInfo(Variant::INT, "id")));
}
