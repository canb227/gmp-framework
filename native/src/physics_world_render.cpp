// GMPPhysicsWorld: drawing the items. See physics_world.h.
//
// Every part of every item kind is one multimesh with one instance per item. Each physics tick the
// transforms of the items are written into the multimesh's buffer (this tick's and last tick's), and
// Godot's physics interpolation draws them in between on every rendered frame. A kind none of whose items
// moved for a tick isn't uploaded again, so a settled pile costs nothing.

#include "physics_world.h"

#include <godot_cpp/classes/rendering_server.hpp>
#include <godot_cpp/classes/world3d.hpp>

using namespace godot;

// Writes a transform as the 12 floats a multimesh buffer holds per instance: the 3x4 matrix row by row,
// each row being a basis row followed by that coordinate of the origin.
static void write_transform(float *to, const Transform3D &t) {
	to[0] = t.basis.rows[0].x;
	to[1] = t.basis.rows[0].y;
	to[2] = t.basis.rows[0].z;
	to[3] = t.origin.x;
	to[4] = t.basis.rows[1].x;
	to[5] = t.basis.rows[1].y;
	to[6] = t.basis.rows[1].z;
	to[7] = t.origin.y;
	to[8] = t.basis.rows[2].x;
	to[9] = t.basis.rows[2].y;
	to[10] = t.basis.rows[2].z;
	to[11] = t.origin.z;
}

void GMPPhysicsWorld::update_rendering() {
	if (!is_inside_tree()) {
		return;
	}
	for (ItemKind &kind : kinds) {
		// Still for more than a tick: the last upload already has this tick's and last tick's transforms equal.
		if (kind.changedTick + 1 < tick) {
			continue;
		}
		for (ItemPart &part : kind.parts) {
			update_part_rendering(kind, part);
		}
	}
}

void GMPPhysicsWorld::update_part_rendering(ItemKind &kind, ItemPart &part) {
	RenderingServer *rs = RenderingServer::get_singleton();

	if (!part.multimesh.is_valid()) {
		RID mesh = part.mesh->get_rid();
		if (!part.surfaceMaterials.empty()) {
			part.meshCopy = rs->mesh_create();
			for (int surface = 0; surface < rs->mesh_get_surface_count(mesh); surface++) {
				rs->mesh_add_surface(part.meshCopy, rs->mesh_get_surface(mesh, surface));
				RID material = rs->mesh_surface_get_material(mesh, surface);
				if (surface < (int)part.surfaceMaterials.size() && part.surfaceMaterials[surface].is_valid()) {
					material = part.surfaceMaterials[surface]->get_rid();
				}
				rs->mesh_surface_set_material(part.meshCopy, surface, material);
			}
			mesh = part.meshCopy;
		}
		part.multimesh = rs->multimesh_create();
		rs->multimesh_set_mesh(part.multimesh, mesh);
		rs->multimesh_set_physics_interpolated(part.multimesh, true);

		part.instance = rs->instance_create();
		rs->instance_set_base(part.instance, part.multimesh);
		rs->instance_set_scenario(part.instance, get_world_3d()->get_scenario());
		// The item transforms are world space, so the instance stays at the world origin.
		rs->instance_set_transform(part.instance, Transform3D());
		if (kind.castShadows) {
			rs->instance_geometry_set_cast_shadows_setting(part.instance, RenderingServer::SHADOW_CASTING_SETTING_ON);
		} else {
			rs->instance_geometry_set_cast_shadows_setting(part.instance, RenderingServer::SHADOW_CASTING_SETTING_OFF);
		}
		if (part.materialOverride.is_valid()) {
			rs->instance_geometry_set_material_override(part.instance, part.materialOverride->get_rid());
		}
	}

	int count = (int)kind.slots.size();
	if (count > part.capacity) {
		// Room for twice as many, so a growing pile doesn't reallocate every tick.
		int capacity = 64;
		while (capacity < count) {
			capacity *= 2;
		}
		part.capacity = capacity;
		rs->multimesh_allocate_data(part.multimesh, capacity, RenderingServer::MULTIMESH_TRANSFORM_3D);
		part.currentBuffer.resize(capacity * 12);
		part.previousBuffer.resize(capacity * 12);
		// Unused instances are drawn nowhere: visible_instances stops before them.
		part.currentBuffer.fill(0);
		part.previousBuffer.fill(0);
	}

	float *current = part.currentBuffer.ptrw();
	float *previous = part.previousBuffer.ptrw();
	for (int i = 0; i < count; i++) {
		const Body &item = bodies[kind.slots[i]];
		Transform3D now = item.current * part.offset;
		write_transform(current + i * 12, now);
		if (item.movedTick == tick) {
			write_transform(previous + i * 12, item.previous * part.offset);
		} else {
			// Didn't move this tick: it stood where it is.
			write_transform(previous + i * 12, now);
		}
	}
	rs->multimesh_set_buffer_interpolated(part.multimesh, part.currentBuffer, part.previousBuffer);
	if (count != part.visibleCount) {
		part.visibleCount = count;
		rs->multimesh_set_visible_instances(part.multimesh, count);
	}
}

void GMPPhysicsWorld::free_rendering() {
	RenderingServer *rs = RenderingServer::get_singleton();
	if (rs == nullptr) {
		// The engine is shutting down and has freed everything already.
		return;
	}
	for (ItemKind &kind : kinds) {
		// Drawn again from scratch if the world comes back into the tree.
		kind.changedTick = tick;
		for (ItemPart &part : kind.parts) {
			if (part.instance.is_valid()) {
				rs->free_rid(part.instance);
			}
			if (part.multimesh.is_valid()) {
				rs->free_rid(part.multimesh);
			}
			if (part.meshCopy.is_valid()) {
				rs->free_rid(part.meshCopy);
			}
			part.instance = RID();
			part.multimesh = RID();
			part.meshCopy = RID();
			part.capacity = 0;
			part.visibleCount = 0;
		}
	}
}
