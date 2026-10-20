// GMPPhysicsWorld: ray casts and overlap tests. See physics_world.h.
//
// Each result says what was found as an item id or a body node, like the events do. Sensor shapes are
// never found: they are volumes for reporting, not things in the world.

#include "physics_world.h"

#include "math_convert.h"
#include "physics_body.h"
#include "physics_shape.h"

#include <algorithm>
#include <vector>

using namespace godot;

static b3QueryFilter query_filter(int64_t mask) {
	b3QueryFilter filter = b3DefaultQueryFilter();
	filter.maskBits = (uint64_t)(uint32_t)mask;
	return filter;
}

// ---- ray cast ----

struct RayContext {
	b3ShapeId shapeId = b3_nullShapeId;
	b3Pos point;
	b3Vec3 normal;
	float fraction = 1.0f;
	uint64_t tags = 0;
	bool hit = false;
};

// Called by Box3D for each shape the ray crosses. Returning the fraction keeps only hits closer than this
// one from now on; returning -1 skips the shape.
static float ray_callback(b3ShapeId shapeId, b3Pos point, b3Vec3 normal, float fraction, uint64_t userMaterialId,
		int triangleIndex, int childIndex, void *context) {
	if (b3Shape_IsSensor(shapeId)) {
		return -1.0f;
	}
	// A ray that starts inside a shape hits it at once, with no normal (a ray from the camera starts inside the
	// player's own capsule). It isn't something the ray found: skip it.
	if (normal.x == 0 && normal.y == 0 && normal.z == 0) {
		return -1.0f;
	}
	RayContext *ray = (RayContext *)context;
	ray->shapeId = shapeId;
	ray->point = point;
	ray->normal = normal;
	ray->fraction = fraction;
	ray->tags = userMaterialId;
	ray->hit = true;
	return fraction;
}

// The closest thing between from and to: { hit, position, normal, fraction, id (the body's), shape (its number
// in the body), tags }.
Dictionary GMPPhysicsWorld::raycast(Vector3 from, Vector3 to, int64_t mask) {
	Dictionary result;
	result["hit"] = false;
	if (!b3World_IsValid(world)) {
		return result;
	}
	RayContext ray;
	b3World_CastRay(world, to_b3(from), to_b3(to - from), query_filter(mask), ray_callback, &ray);
	if (!ray.hit) {
		return result;
	}
	Body *body = body_of_shape(ray.shapeId);
	result["hit"] = true;
	result["position"] = Vector3(ray.point.x, ray.point.y, ray.point.z);
	result["normal"] = to_godot(ray.normal);
	result["fraction"] = ray.fraction;
	result["tags"] = (int64_t)ray.tags;
	result["id"] = (int64_t)0;
	if (body != nullptr) {
		result["id"] = (int64_t)body->id;
	}
	result["shape"] = shape_index(ray.shapeId);
	return result;
}

// ---- overlaps ----

struct OverlapContext {
	GMPPhysicsWorld *world = nullptr;
	PackedInt64Array ids;
};

static bool overlap_callback(b3ShapeId shapeId, void *context) {
	if (b3Shape_IsSensor(shapeId)) {
		return true;
	}
	OverlapContext *overlap = (OverlapContext *)context;
	Body *body = overlap->world->body_of_shape(shapeId);
	// A body with several shapes in the volume is listed once.
	if (body != nullptr && body->id != 0 && !overlap->ids.has((int64_t)body->id)) {
		overlap->ids.push_back((int64_t)body->id);
	}
	// Keep going: we want everything.
	return true;
}

// Everything inside the sphere: the ids of the bodies in it.
PackedInt64Array GMPPhysicsWorld::overlap_sphere(Vector3 center, float radius, int64_t mask) {
	OverlapContext overlap;
	overlap.world = this;
	if (!b3World_IsValid(world)) {
		return overlap.ids;
	}
	// A shape proxy is points around the origin, puffed out by radius: one point makes a sphere.
	b3Vec3 point = { 0, 0, 0 };
	b3ShapeProxy proxy = { &point, 1, radius };
	b3World_OverlapShape(world, to_b3(center), &proxy, query_filter(mask), overlap_callback, &overlap);
	return overlap.ids;
}

// Everything inside the box of the given full size, placed at transform: the ids of the bodies in it.
PackedInt64Array GMPPhysicsWorld::overlap_box(Transform3D transform, Vector3 size, int64_t mask) {
	OverlapContext overlap;
	overlap.world = this;
	if (!b3World_IsValid(world)) {
		return overlap.ids;
	}
	// The box's eight corners, turned with it, around its centre.
	Vector3 half = size / 2;
	Basis rotation = transform.basis.orthonormalized();
	b3Vec3 corners[8];
	int corner = 0;
	for (int x = -1; x <= 1; x += 2) {
		for (int y = -1; y <= 1; y += 2) {
			for (int z = -1; z <= 1; z += 2) {
				corners[corner] = to_b3(rotation.xform(Vector3(half.x * x, half.y * y, half.z * z)));
				corner++;
			}
		}
	}
	b3ShapeProxy proxy = { corners, 8, 0.0f };
	b3World_OverlapShape(world, to_b3(transform.origin), &proxy, query_filter(mask), overlap_callback, &overlap);
	return overlap.ids;
}

// Everything the shape would overlap at transform: the ids of the bodies in it. Boxes, spheres,
// capsules and hulls are tested as they are; cylinders and meshes as the box around them, which is close
// enough for checking where a structure fits.
PackedInt64Array GMPPhysicsWorld::overlap_shape(Object *shapeObject, Transform3D transform, int64_t mask, float margin) {
	OverlapContext overlap;
	overlap.world = this;
	GMPShape *shape = Object::cast_to<GMPShape>(shapeObject);
	if (shape == nullptr || !b3World_IsValid(world)) {
		return overlap.ids;
	}
	Vector3 scale = transform.basis.get_scale();
	Basis rotation = transform.basis.orthonormalized();
	float largestScale = std::max(scale.x, std::max(scale.y, scale.z));

	// The proxy: points around transform.origin, puffed out by radius.
	std::vector<b3Vec3> points;
	float radius = 0;
	if (shape->shape == GMPShape::SPHERE) {
		points.push_back(b3Vec3{ 0, 0, 0 });
		radius = std::max(0.001f, shape->radius * largestScale - margin);
	} else if (shape->shape == GMPShape::CAPSULE) {
		radius = std::max(0.001f, shape->radius * largestScale - margin);
		float halfSegment = std::max(0.0f, shape->height * scale.y / 2 - radius);
		Vector3 axis = rotation.xform(Vector3(0, 1, 0));
		points.push_back(to_b3(-axis * halfSegment));
		points.push_back(to_b3(axis * halfSegment));
	} else if (shape->shape == GMPShape::HULL && shape->mesh.is_valid()) {
		// The hull's own corners (at most the shape's max_hull_vertices, as its collider has; a shape proxy takes up
		// to B3_MAX_HULL_VERTICES), each pulled in by margin toward the hull's middle.
		PackedVector3Array faces = shape->mesh->get_faces();
		std::vector<b3Vec3> meshPoints;
		for (int i = 0; i < faces.size(); i++) {
			meshPoints.push_back(to_b3(transform.basis.xform(faces[i])));
		}
		b3HullData *hull = nullptr;
		if (!meshPoints.empty()) {
			hull = b3CreateHull(meshPoints.data(), (int)meshPoints.size(), shape->maxHullVertices);
		}
		if (hull == nullptr) {
			return overlap.ids;
		}
		const b3Vec3 *corners = b3GetHullPoints(hull);
		Vector3 centre = to_godot(hull->center);
		for (int i = 0; i < hull->vertexCount; i++) {
			Vector3 point = to_godot(corners[i]);
			Vector3 out = point - centre;
			float length = out.length();
			if (length > margin) {
				point = centre + out * ((length - margin) / length);
			} else {
				point = centre;
			}
			points.push_back(to_b3(point));
		}
		b3DestroyHull(hull);
	} else {
		// A box: the box shape itself, or the box around a cylinder or mesh.
		AABB box = AABB(-shape->size / 2, shape->size);
		if (shape->shape == GMPShape::CYLINDER) {
			box = AABB(Vector3(-shape->radius, -shape->height / 2, -shape->radius), Vector3(shape->radius * 2, shape->height, shape->radius * 2));
		} else if (shape->shape == GMPShape::MESH && shape->mesh.is_valid()) {
			box = shape->mesh->get_aabb();
		}
		// Pulled in by margin on every side, in metres whatever the scale: the box is shrunk about its own centre
		// after scaling.
		Vector3 centre = transform.basis.xform(box.get_center());
		for (int corner = 0; corner < 8; corner++) {
			// transform.basis turns and scales each corner (but doesn't move it: the origin is the query's).
			Vector3 point = transform.basis.xform(box.get_endpoint(corner));
			Vector3 out = point - centre;
			Vector3 inward;
			for (int axis = 0; axis < 3; axis++) {
				// Along each of the box's own (turned) axes, take margin off the corner's offset from the centre.
				Vector3 boxAxis = rotation.get_column(axis);
				float along = out.dot(boxAxis);
				float shrunk = std::max(0.0f, std::abs(along) - margin);
				if (along < 0) {
					shrunk = -shrunk;
				}
				inward += boxAxis * shrunk;
			}
			points.push_back(to_b3(centre + inward));
		}
	}
	b3ShapeProxy proxy = { points.data(), (int)points.size(), radius };
	b3World_OverlapShape(world, to_b3(transform.origin), &proxy, query_filter(mask), overlap_callback, &overlap);
	return overlap.ids;
}
