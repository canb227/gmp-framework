#pragma once

#include <godot_cpp/classes/camera3d.hpp>
#include <godot_cpp/classes/editor_node3d_gizmo.hpp>
#include <godot_cpp/classes/editor_node3d_gizmo_plugin.hpp>
#include <godot_cpp/classes/editor_plugin.hpp>

// Editor only. Draws every GMPShape's outline (while its debug_visible is on; sensors in orange) and gives box,
// sphere, capsule and cylinder shapes handles to resize them with the mouse, like the CSG nodes: drag a handle to
// move that face while the opposite one stays put, hold Alt to resize both sides around the middle, hold Ctrl to
// snap to 0.1 m. Every resize is one undo step.
class GMPShapeGizmoPlugin : public godot::EditorNode3DGizmoPlugin {
	GDCLASS(GMPShapeGizmoPlugin, godot::EditorNode3DGizmoPlugin)

public:
	// Called once the editor is up (its settings give the colours).
	void create_materials();

	bool _has_gizmo(godot::Node3D *node) const override;
	godot::String _get_gizmo_name() const override;
	void _redraw(const godot::Ref<godot::EditorNode3DGizmo> &gizmo) override;
	godot::String _get_handle_name(const godot::Ref<godot::EditorNode3DGizmo> &gizmo, int32_t id, bool secondary) const override;
	godot::Variant _get_handle_value(const godot::Ref<godot::EditorNode3DGizmo> &gizmo, int32_t id, bool secondary) const override;
	void _begin_handle_action(const godot::Ref<godot::EditorNode3DGizmo> &gizmo, int32_t id, bool secondary) override;
	void _set_handle(const godot::Ref<godot::EditorNode3DGizmo> &gizmo, int32_t id, bool secondary, godot::Camera3D *camera, const godot::Vector2 &screenPos) override;
	void _commit_handle(const godot::Ref<godot::EditorNode3DGizmo> &gizmo, int32_t id, bool secondary, const godot::Variant &restore, bool cancel) override;

protected:
	static void _bind_methods() {}

private:
	// The shape as the drag began: a one-sided resize moves the shape from there, and the mouse ray is measured
	// against where it was.
	godot::Transform3D startTransform;
	godot::Transform3D startGlobalTransform;
	godot::Vector3 startSize;
	float startHeight = 0;
};

// Adds the gizmo plugin to the editor.
class GMPShapeEditorPlugin : public godot::EditorPlugin {
	GDCLASS(GMPShapeEditorPlugin, godot::EditorPlugin)

protected:
	static void _bind_methods() {}
	void _notification(int what);

private:
	godot::Ref<GMPShapeGizmoPlugin> gizmoPlugin;
};
