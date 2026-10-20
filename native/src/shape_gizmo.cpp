#include "shape_gizmo.h"

#include "physics_shape.h"

#include <godot_cpp/classes/editor_interface.hpp>
#include <godot_cpp/classes/editor_settings.hpp>
#include <godot_cpp/classes/editor_undo_redo_manager.hpp>
#include <godot_cpp/classes/geometry3d.hpp>
#include <godot_cpp/classes/input.hpp>
#include <godot_cpp/classes/material.hpp>
#include <godot_cpp/classes/standard_material3d.hpp>
#include <godot_cpp/core/math.hpp>

#include <algorithm>

using namespace godot;

// Ctrl snaps sizes to this (m), and nothing gets smaller than MinSize.
const float SnapStep = 0.1f;
const float MinSize = 0.001f;

// The handles' ids, by shape:
//   box                  axis * 2 on the positive face, axis * 2 + 1 on the negative one (axis 0 X, 1 Y, 2 Z)
//   sphere               0, the radius, on +X
//   capsule, cylinder    0 the radius on +X, 1 the top, 2 the bottom
const int RadiusHandle = 0;
const int TopHandle = 1;
const int BottomHandle = 2;

// Where a shape's handles are, in its own space, in id order. None for hulls and meshes.
static PackedVector3Array handle_positions(const GMPShape *shape) {
	PackedVector3Array handles;
	if (shape->shape == GMPShape::BOX) {
		for (int axis = 0; axis < 3; axis++) {
			Vector3 face;
			face[axis] = shape->size[axis] / 2;
			handles.push_back(face);
			handles.push_back(-face);
		}
	} else if (shape->shape == GMPShape::SPHERE) {
		handles.push_back(Vector3(shape->radius, 0, 0));
	} else if (shape->shape == GMPShape::CAPSULE || shape->shape == GMPShape::CYLINDER) {
		handles.push_back(Vector3(shape->radius, 0, 0));
		handles.push_back(Vector3(0, shape->height / 2, 0));
		handles.push_back(Vector3(0, -shape->height / 2, 0));
	}
	return handles;
}

// The property a handle changes.
static StringName handle_property(const GMPShape *shape, int id) {
	if (shape->shape == GMPShape::BOX) {
		return "size";
	}
	if (shape->shape == GMPShape::SPHERE || id == RadiusHandle) {
		return "radius";
	}
	return "height";
}

void GMPShapeGizmoPlugin::create_materials() {
	Color shapeColor = Color(0.5, 0.7, 1.0);
	Ref<EditorSettings> settings = EditorInterface::get_singleton()->get_editor_settings();
	if (settings.is_valid() && settings->has_setting("editors/3d_gizmos/gizmo_colors/shape")) {
		shapeColor = settings->get_setting("editors/3d_gizmos/gizmo_colors/shape");
	}
	create_material("shape", shapeColor);
	create_material("sensor", Color(1.0, 0.75, 0.2));
	create_handle_material("handles");
}

bool GMPShapeGizmoPlugin::_has_gizmo(Node3D *node) const {
	return Object::cast_to<GMPShape>(node) != nullptr;
}

String GMPShapeGizmoPlugin::_get_gizmo_name() const {
	return "GMPShape";
}

void GMPShapeGizmoPlugin::_redraw(const Ref<EditorNode3DGizmo> &gizmo) {
	gizmo->clear();
	GMPShape *shape = Object::cast_to<GMPShape>(gizmo->get_node_3d());
	if (shape == nullptr) {
		return;
	}
	if (shape->debugVisible) {
		PackedVector3Array lines = shape->get_debug_lines();
		if (!lines.is_empty()) {
			String materialName = "shape";
			if (shape->isSensor) {
				materialName = "sensor";
			}
			gizmo->add_lines(lines, get_material(materialName, gizmo));
			// So clicking the outline in the viewport selects the shape.
			gizmo->add_collision_segments(lines);
		}
	}
	// The handles show while the shape is selected, outline or not.
	PackedVector3Array handles = handle_positions(shape);
	if (!handles.is_empty()) {
		gizmo->add_handles(handles, get_material("handles", gizmo), PackedInt32Array());
	}
}

String GMPShapeGizmoPlugin::_get_handle_name(const Ref<EditorNode3DGizmo> &gizmo, int32_t id, bool secondary) const {
	GMPShape *shape = Object::cast_to<GMPShape>(gizmo->get_node_3d());
	if (shape == nullptr) {
		return "";
	}
	if (shape->shape == GMPShape::BOX) {
		const char *axisNames[3] = { "Size X", "Size Y", "Size Z" };
		return axisNames[std::clamp(id / 2, 0, 2)];
	}
	if (handle_property(shape, id) == StringName("radius")) {
		return "Radius";
	}
	return "Height";
}

Variant GMPShapeGizmoPlugin::_get_handle_value(const Ref<EditorNode3DGizmo> &gizmo, int32_t id, bool secondary) const {
	GMPShape *shape = Object::cast_to<GMPShape>(gizmo->get_node_3d());
	if (shape == nullptr) {
		return Variant();
	}
	return shape->get(handle_property(shape, id));
}

void GMPShapeGizmoPlugin::_begin_handle_action(const Ref<EditorNode3DGizmo> &gizmo, int32_t id, bool secondary) {
	GMPShape *shape = Object::cast_to<GMPShape>(gizmo->get_node_3d());
	if (shape == nullptr) {
		return;
	}
	startTransform = shape->get_transform();
	startGlobalTransform = shape->get_global_transform();
	startSize = shape->size;
	startHeight = shape->height;
}

// The point on one of the shape's axes (through its middle) nearest the mouse ray, in the shape's own space as the
// drag began.
static Vector3 point_on_axis(const Transform3D &shapeGlobal, Camera3D *camera, const Vector2 &screenPos, int axis) {
	Transform3D toShape = shapeGlobal.affine_inverse();
	Vector3 rayFrom = camera->project_ray_origin(screenPos);
	Vector3 rayDirection = camera->project_ray_normal(screenPos);
	Vector3 axisEnd;
	axisEnd[axis] = 4096;
	PackedVector3Array closest = Geometry3D::get_singleton()->get_closest_points_between_segments(
			-axisEnd, axisEnd, toShape.xform(rayFrom), toShape.xform(rayFrom + rayDirection * 4096));
	return closest[0];
}

static float snapped_length(float length) {
	if (Input::get_singleton()->is_key_pressed(KEY_CTRL)) {
		length = Math::snapped(length, SnapStep);
	}
	return std::max(length, MinSize);
}

// The new length along an axis from dragging the handle at one end of it (sign +1 for the positive end) to
// handleAt, along the axis. Sets middleShift, how far the shape's middle moves along the axis so the other end
// stays where it was; with Alt both ends move and the middle stays.
static float dragged_length(float startLength, float sign, float handleAt, float &middleShift) {
	bool bothEnds = Input::get_singleton()->is_key_pressed(KEY_ALT);
	float length;
	if (bothEnds) {
		length = handleAt * sign * 2;
	} else {
		length = sign * handleAt + startLength / 2;
	}
	length = snapped_length(length);
	middleShift = 0;
	if (!bothEnds) {
		middleShift = sign * (length - startLength) / 2;
	}
	return length;
}

void GMPShapeGizmoPlugin::_set_handle(const Ref<EditorNode3DGizmo> &gizmo, int32_t id, bool secondary, Camera3D *camera, const Vector2 &screenPos) {
	GMPShape *shape = Object::cast_to<GMPShape>(gizmo->get_node_3d());
	if (shape == nullptr || camera == nullptr) {
		return;
	}

	if (shape->shape == GMPShape::BOX) {
		int axis = std::clamp(id / 2, 0, 2);
		float sign = 1.0f;
		if (id % 2 == 1) {
			sign = -1.0f;
		}
		Vector3 point = point_on_axis(startGlobalTransform, camera, screenPos, axis);
		float middleShift = 0;
		Vector3 size = startSize;
		size[axis] = dragged_length(startSize[axis], sign, point[axis], middleShift);
		Vector3 shift;
		shift[axis] = middleShift;
		shape->set_size(size);
		shape->set_position(startTransform.xform(shift));
		return;
	}

	if (shape->shape == GMPShape::SPHERE || id == RadiusHandle) {
		Vector3 point = point_on_axis(startGlobalTransform, camera, screenPos, 0);
		shape->set_radius(snapped_length(point.x));
		return;
	}

	// A capsule's or cylinder's top or bottom.
	float sign = 1.0f;
	if (id == BottomHandle) {
		sign = -1.0f;
	}
	Vector3 point = point_on_axis(startGlobalTransform, camera, screenPos, 1);
	float middleShift = 0;
	float height = dragged_length(startHeight, sign, point.y, middleShift);
	shape->set_height(height);
	shape->set_position(startTransform.xform(Vector3(0, middleShift, 0)));
}

void GMPShapeGizmoPlugin::_commit_handle(const Ref<EditorNode3DGizmo> &gizmo, int32_t id, bool secondary, const Variant &restore, bool cancel) {
	GMPShape *shape = Object::cast_to<GMPShape>(gizmo->get_node_3d());
	if (shape == nullptr) {
		return;
	}
	StringName property = handle_property(shape, id);
	if (cancel) {
		shape->set(property, restore);
		shape->set_transform(startTransform);
		return;
	}
	EditorUndoRedoManager *undoRedo = EditorInterface::get_singleton()->get_editor_undo_redo();
	undoRedo->create_action("Resize GMPShape");
	undoRedo->add_do_property(shape, property, shape->get(property));
	undoRedo->add_do_property(shape, "transform", shape->get_transform());
	undoRedo->add_undo_property(shape, property, restore);
	undoRedo->add_undo_property(shape, "transform", startTransform);
	undoRedo->commit_action();
}

void GMPShapeEditorPlugin::_notification(int what) {
	if (what == NOTIFICATION_ENTER_TREE) {
		gizmoPlugin.instantiate();
		gizmoPlugin->create_materials();
		add_node_3d_gizmo_plugin(gizmoPlugin);
	}
	if (what == NOTIFICATION_EXIT_TREE) {
		if (gizmoPlugin.is_valid()) {
			remove_node_3d_gizmo_plugin(gizmoPlugin);
			gizmoPlugin.unref();
		}
	}
}
