#pragma once

#include <box3d/box3d.h>

#include <godot_cpp/classes/mesh.hpp>
#include <godot_cpp/classes/mesh_instance3d.hpp>
#include <godot_cpp/classes/node3d.hpp>
#include <godot_cpp/variant/packed_vector3_array.hpp>

#include <cstdint>

class GMPBody;

// The geometry of one convex collider, in its own axes: what a GMPShape's properties or an item kind's
// description give. type is a GMPShape::ShapeType (any but MESH).
struct ShapeGeometry {
	int type = 0;
	godot::Vector3 size = godot::Vector3(1, 1, 1);
	float radius = 0.5f;
	float height = 2.0f;
	int sides = 16;
	// Hull: the points it wraps, and at most how many corners it gets.
	godot::PackedVector3Array hullPoints;
	int maxHullVertices = 128;
};

// A convex collider placed in its body (moved, turned and scaled there), ready to make Box3D shapes from. A
// GMPShape places its geometry each time it attaches; an item kind places each of its colliders once, and
// every item of the kind makes its shapes from that.
struct PlacedShape {
	// Box uses box, sphere sphere, capsule capsule, and cylinder and hull use hull.
	int type = 0;
	b3BoxHull box;
	b3Sphere sphere;
	b3Capsule capsule;
	// Owned: free_placed_shape frees it. Box3D keeps its own copy of every hull a shape is made from.
	b3HullData *hull = nullptr;
};

// Places geometry at local (where it sits in the body, scale included). False if no shape can be made from it,
// like a hull from too few points.
bool place_shape(const ShapeGeometry &geometry, const godot::Transform3D &local, PlacedShape &placed);
b3ShapeId create_placed_shape(b3BodyId bodyId, const b3ShapeDef &def, const PlacedShape &placed);
void free_placed_shape(PlacedShape &placed);

// One collider of a GMPBody: placed as a child (or deeper descendant) of the body node, at its own
// position and rotation in the body. A body is made of all the shapes under it, so a structure with
// walls and a floor is one body with several box shapes.
//
// A sensor shape (is_sensor) doesn't collide: it reports the bodies (loose items and nodes alike, by id) that
// enter and leave it with the entered and exited signals, and lists what is inside it now.
class GMPShape : public godot::Node3D {
	GDCLASS(GMPShape, godot::Node3D)

public:
	enum ShapeType {
		BOX = 0,
		SPHERE = 1,
		// Capsule and cylinder stand along the shape's Y axis.
		CAPSULE = 2,
		CYLINDER = 3,
		// A convex hull around the mesh's points. The only mesh-based shape a dynamic body can use.
		HULL = 4,
		// The mesh's triangles exactly. Box3D only collides these on static bodies.
		MESH = 5,
	};

	// ---- what it is ----
	ShapeType shape = BOX;
	// Box: full size.
	godot::Vector3 size = godot::Vector3(1, 1, 1);
	// Sphere, capsule and cylinder.
	float radius = 0.5f;
	// Capsule and cylinder: the whole length, end to end.
	float height = 2.0f;
	// Cylinder: sides around.
	int sides = 16;
	// Hull and mesh.
	godot::Ref<godot::Mesh> mesh;
	// Hull: at most this many corners (4 to 128). Box3D adds the mesh's points farthest out first, so a lower
	// count keeps the overall shape and drops the fine detail, which makes contacts cheaper.
	int maxHullVertices = 128;

	// ---- its surface ----
	// kg per cubic metre (water is 1000). Gives a dynamic body its mass.
	float density = 1000.0f;
	float friction = 0.6f;
	float restitution = 0.0f;
	float rollingResistance = 0.0f;
	// Drives what touches it along the surface, like a belt (m/s, in the shape's own axes).
	godot::Vector3 tangentVelocity;
	// What it is made of and does (see tags.h). In the world it also carries the tags its body was given.
	uint64_t tags = 0;
	bool isSensor = false;

	// Its number in its body, in tree order (set by the body), so a hit can name it the same on every peer.
	int bodyShapeIndex = -1;

	// ---- seeing it ----
	// Draws the collider's outline: in the editor (its gizmo), and in a running game with Debug > Visible
	// Collision Shapes on.
	bool debugVisible = true;

	GMPShape();
	~GMPShape();

	// Creates the Box3D shape on body (called by GMPBody once its Box3D body exists), and removes it.
	void attach(GMPBody *body);
	void detach();
	b3ShapeId get_shape_id() const;

	// Sensors: what is inside now.
	godot::PackedInt64Array get_overlapping() const;

	void set_shape(int value);
	int get_shape() const;
	void set_size(godot::Vector3 value);
	godot::Vector3 get_size() const;
	void set_radius(float value);
	float get_radius() const;
	void set_height(float value);
	float get_height() const;
	void set_sides(int value);
	int get_sides() const;
	void set_mesh(const godot::Ref<godot::Mesh> &value);
	godot::Ref<godot::Mesh> get_mesh() const;
	void set_max_hull_vertices(int value);
	int get_max_hull_vertices() const;
	void set_density(float value);
	float get_density() const;
	void set_friction(float value);
	float get_friction() const;
	void set_restitution(float value);
	float get_restitution() const;
	void set_rolling_resistance(float value);
	float get_rolling_resistance() const;
	void set_tangent_velocity(godot::Vector3 value);
	godot::Vector3 get_tangent_velocity() const;
	void set_tags(int64_t value);
	int64_t get_tags() const;
	void set_is_sensor(bool value);
	bool get_is_sensor() const;
	void set_debug_visible(bool value);
	bool get_debug_visible() const;

	// The collider's outline as line segments (pairs of points) in the shape's own space, for drawing it.
	godot::PackedVector3Array get_debug_lines() const;

protected:
	static void _bind_methods();
	void _notification(int what);

private:
	GMPBody *attachedBody = nullptr;
	b3ShapeId shapeId = b3_nullShapeId;
	// Mesh shapes only: Box3D uses this while the shape exists, so it is kept here and freed after.
	b3MeshData *meshData = nullptr;
	// In a running game with visible collision shapes: an internal child drawing the outline.
	godot::MeshInstance3D *debugMesh = nullptr;

	// The body this shape belongs to: the nearest GMPBody above it.
	GMPBody *find_body() const;
	// Remakes the Box3D shape and redraws the outline after a property change.
	void rebuild();
	// Redraws the editor gizmo, or the outline in a running game (or removes it when it shouldn't show).
	void update_debug_drawing();
};

VARIANT_ENUM_CAST(GMPShape::ShapeType);
