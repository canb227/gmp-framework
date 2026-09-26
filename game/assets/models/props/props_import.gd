@tool
extends EditorScenePostImport
## Post-import script for the salvage machine / tool / ore .glb files (built by source/build_props.py in Blender).
## - Albedo lives in the vertex colours, as in the conveyor and level exports.
## - "M_PropGlass" becomes see-through cyan-tinted glass (the spawner's tube).
## - "M_VoidVortex" becomes the swirling void floor shader.

const VORTEX_SHADER := preload("res://game/assets/shaders/VoidVortex.gdshader")

func _post_import(scene: Node) -> Object:
	_walk(scene)
	return scene

func _walk(n: Node) -> void:
	if n is MeshInstance3D and (n as MeshInstance3D).mesh:
		var mi := n as MeshInstance3D
		var mesh := mi.mesh
		for s in mesh.get_surface_count():
			var mat := mesh.surface_get_material(s)
			if mat == null:
				continue
			if mat.resource_name == "M_VoidVortex":
				var sm := ShaderMaterial.new()
				sm.shader = VORTEX_SHADER
				mesh.surface_set_material(s, sm)
				mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
			elif mat.resource_name == "M_PropGlass":
				var g := StandardMaterial3D.new()
				g.resource_name = "M_PropGlass"
				g.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
				g.cull_mode = BaseMaterial3D.CULL_DISABLED
				g.albedo_color = Color(0.45, 0.85, 1.0, 0.16)
				g.metallic = 0.3
				g.roughness = 0.05
				g.rim_enabled = true
				g.rim = 0.6
				g.emission_enabled = true
				g.emission = Color(0.1, 0.4, 0.5)
				g.emission_energy_multiplier = 0.4
				mesh.surface_set_material(s, g)
				mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
			elif mat is StandardMaterial3D:
				var sm := mat as StandardMaterial3D
				sm.vertex_color_use_as_albedo = true
				sm.albedo_color = Color.WHITE
	for c in n.get_children():
		_walk(c)
