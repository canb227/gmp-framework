#pragma once

#include <box3d/box3d.h>

#include <godot_cpp/classes/mesh.hpp>
#include <godot_cpp/classes/node3d.hpp>

class GMPBody;

// One collider of a GMPBody: placed as a child (or deeper descendant) of the body node, at its own
// position and rotation in the body. A body is made of all the shapes under it, so a structure with
// walls and a floor is one body with several box shapes.
//
// A sensor shape (is_sensor) doesn't collide: it reports what enters and leaves it, with the item_entered
// and body_entered signals (and their _exited pairs), and lists what is inside it now.
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

	// ---- its surface ----
	// kg per cubic metre (water is 1000). Gives a dynamic body its mass.
	float density = 1000.0f;
	float friction = 0.6f;
	float restitution = 0.0f;
	float rollingResistance = 0.0f;
	// Drives what touches it along the surface, like a belt (m/s, in the shape's own axes).
	godot::Vector3 tangentVelocity;
	// What it is made of, for impact sounds and the like. Hits report both sides' surface types.
	int64_t surfaceType = 0;
	bool isSensor = false;

	GMPShape();
	~GMPShape();

	// Creates the Box3D shape on body (called by GMPBody once its Box3D body exists), and removes it.
	void attach(GMPBody *body);
	void detach();
	b3ShapeId get_shape_id() const;

	// Sensors: what is inside now.
	godot::PackedInt64Array get_overlapping_items() const;
	godot::Array get_overlapping_bodies() const;

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
	void set_surface_type(int64_t value);
	int64_t get_surface_type() const;
	void set_is_sensor(bool value);
	bool get_is_sensor() const;

protected:
	static void _bind_methods();
	void _notification(int what);

private:
	GMPBody *attachedBody = nullptr;
	b3ShapeId shapeId = b3_nullShapeId;
	// Mesh shapes only: Box3D uses this while the shape exists, so it is kept here and freed after.
	b3MeshData *meshData = nullptr;

	// The body this shape belongs to: the nearest GMPBody above it.
	GMPBody *find_body() const;
	// Remakes the Box3D shape after a property change.
	void rebuild();
};

VARIANT_ENUM_CAST(GMPShape::ShapeType);
