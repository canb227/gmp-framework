@tool
extends EditorScenePostImport
## Post-import script for the salvage conveyor .glb files (built by source/build_conveyors.py in Blender).
## - Albedo lives in the vertex colours (the Blender materials' base colour is just a multiplier), as in the
##   level exports.
## - The belt surface ("M_ConvBelt") becomes the shared scrolling belt material; set the "belt_speed"
##   instance shader parameter on the Belt node to change its speed or direction.

const BELT_MATERIAL := preload("res://game/assets/materials/conveyor_belt.tres")

func _post_import(scene: Node) -> Object:
	_walk(scene)
	return scene

func _walk(n: Node) -> void:
	if n is MeshInstance3D and (n as MeshInstance3D).mesh:
		var mesh := (n as MeshInstance3D).mesh
		for s in mesh.get_surface_count():
			var mat := mesh.surface_get_material(s)
			if mat == null:
				continue
			if mat.resource_name == "M_ConvBelt":
				mesh.surface_set_material(s, BELT_MATERIAL)
			elif mat is StandardMaterial3D:
				var sm := mat as StandardMaterial3D
				sm.vertex_color_use_as_albedo = true
				sm.albedo_color = Color.WHITE
	for c in n.get_children():
		_walk(c)
