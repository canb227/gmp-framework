@tool
extends EditorScenePostImport
## Post-import script shared by the model families built from shared/salvage_lib.py (advanced and magnetic
## conveyors, chutes, launchers, sorting, field projectors, processing machines).
## - Albedo lives in the vertex colours, as in the conveyor, prop and level exports.
## - "M_ConvBelt" becomes the shared scrolling belt material (set the "belt_speed" instance shader parameter).
## - "M_PropGlass" becomes see-through cyan-tinted glass.
## - "M_FieldAntigrav" / "M_FieldZeroPoint" become additive ForceField shells (violet / cyan), no shadows.

const BELT_MATERIAL := preload("res://game/assets/materials/conveyor_belt.tres")
const FIELD_SHADER := preload("res://game/assets/shaders/ForceField.gdshader")

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
			match mat.resource_name:
				"M_ConvBelt":
					mesh.surface_set_material(s, BELT_MATERIAL)
				"M_PropGlass":
					mesh.surface_set_material(s, _glass())
					mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
				"M_FieldAntigrav":
					mesh.surface_set_material(s, _field(Color(0.55, 0.35, 1.0), 0.25))
					mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
				"M_FieldZeroPoint":
					mesh.surface_set_material(s, _field(Color(0.3, 0.9, 1.0), 0.8))
					mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
				_:
					if mat is StandardMaterial3D:
						var sm := mat as StandardMaterial3D
						sm.vertex_color_use_as_albedo = true
						sm.albedo_color = Color.WHITE
	for c in n.get_children():
		_walk(c)

func _glass() -> StandardMaterial3D:
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
	return g

func _field(tint: Color, flow_speed: float) -> ShaderMaterial:
	var m := ShaderMaterial.new()
	m.shader = FIELD_SHADER
	m.set_shader_parameter("tint", tint)
	m.set_shader_parameter("flow_speed", flow_speed)
	return m
