@tool
extends EditorScenePostImport
## Post-import script for test_facility.glb (Godot 4).
## Import dock -> Import Script = this file, Meshes > Light Baking = Static Lightmaps, then Reimport.
## - Every material uses the mesh's vertex colours (panel grime, rock strata) as albedo.
## - Lights -> Static bake mode for LightmapGI; ranges set per light family.
## - Foliage (vines, moss, roots, saplings) is excluded from the lightmap bake (too many thin
##   faces for UV2) and lit dynamically from the baked light probes instead.
## - Tube glass and the frosted observation glass: no shadow casting.
## - Camera_Yaw / Camera_Head keep their pivots; rotate them (yaw around Y, head around its local Z in Godot) to track the player.
##   Camera_ScanBeam and Camera_Eye are children of the head and are left Dynamic so they can move.

const ENERGY_MULTIPLIER := 1.0
const FOLIAGE := ["Vines", "Moss_Grass", "ChasmRoots", "Saplings"]

func _post_import(scene: Node) -> Object:
	_walk(scene)
	return scene

func _range_for(n: String) -> float:
	if n.begins_with("Core_Light"): return 16.0
	if n.begins_with("Chasm_DeepGlow"): return 30.0
	if n.begins_with("Fluo_"): return 26.0
	if n.begins_with("WorkLight"): return 16.0
	if n == "Camera_ScanBeam": return 30.0
	if n.begins_with("Backroom") or n.begins_with("ObsRoom"): return 12.0
	if n.contains("Emergency"): return 6.0
	return 8.0

func _walk(n: Node) -> void:
	if n is Light3D:
		var l := n as Light3D
		var nm := String(l.name)
		l.light_bake_mode = Light3D.BAKE_DYNAMIC if nm.begins_with("Camera_") else Light3D.BAKE_STATIC
		l.shadow_enabled = true
		l.light_energy *= ENERGY_MULTIPLIER
		if l is OmniLight3D: (l as OmniLight3D).omni_range = _range_for(String(l.name))
		if l is SpotLight3D: (l as SpotLight3D).spot_range = _range_for(String(l.name))
	elif n is MeshInstance3D:
		var mi := n as MeshInstance3D
		var nm := String(mi.name)
		var foliage := false
		for f in FOLIAGE:
			if nm.begins_with(f): foliage = true
		mi.gi_mode = GeometryInstance3D.GI_MODE_DYNAMIC if foliage else GeometryInstance3D.GI_MODE_STATIC
		if mi.mesh:
			for s in mi.mesh.get_surface_count():
				var mat := mi.mesh.surface_get_material(s)
				if mat is StandardMaterial3D:
					var sm := mat as StandardMaterial3D
					sm.vertex_color_use_as_albedo = true
					if sm.resource_name in ["M_TubeGlass", "M_FrostedGlass"]:
						mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
					if foliage:
						sm.cull_mode = BaseMaterial3D.CULL_DISABLED
	for c in n.get_children():
		_walk(c)
