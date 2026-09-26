@tool
extends EditorScenePostImport
## Post-import script for the level .glb files exported from Blender (mine_cavern.glb, test_facility.glb).
##
## This project simulates with Box3D (Godot's own 3D physics is set to Dummy), so Godot's built-in
## "-col" / "-colonly" name suffixes are switched off in each .glb's import settings
## (nodes/use_node_type_suffixes = false) and handled here instead:
##   "<name>-col"      the mesh stays visible and becomes the child of a static Box3DBody with a
##                     triangle-mesh collider (shape_type Mesh, built from that child mesh).
##   "<name>-colonly"  invisible proxy -> static Box3DBody box sized to the proxy's bounds; the mesh is dropped.
## It also applies the lighting/material setup the Blender exports expect.

const BODY_STATIC := 0
const SHAPE_BOX := 0
const SHAPE_MESH := 6

## Blender-exported light intensities, rescaled to Godot energy. Tune here, then Reimport.
const OMNI_ENERGY_SCALE := 1.0
const SPOT_ENERGY_SCALE := 1.0
const SUN_ENERGY_SCALE := 1.0
## Omni/spot lights at or above this (post-scale) energy cast shadows; dimmer ones don't, to keep the
## shadow atlas manageable with 60+ lights.
const SHADOW_ENERGY_MIN := 1.0

const FOLIAGE := ["Vines", "Moss_Grass", "ChasmRoots", "Saplings", "GreatTree"]
const NO_SHADOW_MATERIALS := ["M_TubeGlass", "M_FrostedGlass", "M_Water"]

var created_mesh_bodies := 0
var created_box_bodies := 0

func _post_import(scene: Node) -> Object:
	if not ClassDB.class_exists("Box3DBody"):
		push_error("Level import: the Box3D extension isn't loaded, so no colliders were created.")
		return scene
	var col: Array[MeshInstance3D] = []
	var colonly: Array[MeshInstance3D] = []
	_collect(scene, col, colonly)
	for mi in col:
		_make_mesh_body(mi, scene)
	for mi in colonly:
		_make_box_body(mi, scene)
	_setup_visuals(scene)
	print("Level import %s: %d mesh bodies, %d box bodies" % [get_source_file(), created_mesh_bodies, created_box_bodies])
	return scene

func _collect(n: Node, col: Array[MeshInstance3D], colonly: Array[MeshInstance3D]) -> void:
	for c in n.get_children():
		_collect(c, col, colonly)
	if n is MeshInstance3D:
		var nm := String(n.name)
		if nm.ends_with("-colonly"):
			colonly.append(n)
		elif nm.ends_with("-col"):
			col.append(n)

func _new_static_body(nm: String, shape: int) -> Node3D:
	var body: Node3D = ClassDB.instantiate("Box3DBody")
	body.name = nm
	body.set("body_type", BODY_STATIC)
	body.set("shape_type", shape)
	return body

func _make_mesh_body(mi: MeshInstance3D, scene: Node) -> void:
	var parent := mi.get_parent()
	var idx := mi.get_index()
	var body := _new_static_body(String(mi.name).trim_suffix("-col"), SHAPE_MESH)
	body.transform = mi.transform
	parent.add_child(body)
	parent.move_child(body, idx)
	body.owner = scene
	parent.remove_child(mi)
	body.add_child(mi)
	mi.transform = Transform3D.IDENTITY
	mi.name = "Mesh"
	_own(mi, scene)
	created_mesh_bodies += 1

func _make_box_body(mi: MeshInstance3D, scene: Node) -> void:
	var parent := mi.get_parent()
	var idx := mi.get_index()
	var aabb := mi.mesh.get_aabb() if mi.mesh else AABB()
	var body := _new_static_body(String(mi.name).trim_suffix("-colonly"), SHAPE_BOX)
	body.set("box_size", aabb.size * mi.transform.basis.get_scale())
	body.transform = Transform3D(mi.transform.basis.orthonormalized(), mi.transform * aabb.get_center())
	parent.add_child(body)
	parent.move_child(body, idx)
	body.owner = scene
	parent.remove_child(mi)
	mi.free()
	created_box_bodies += 1

func _own(n: Node, scene: Node) -> void:
	n.owner = scene
	for c in n.get_children():
		_own(c, scene)

func _light_range(nm: String) -> float:
	if nm.begins_with("Core_Light"): return 16.0
	if nm.begins_with("Chasm_DeepGlow"): return 30.0
	if nm.begins_with("Fluo_"): return 26.0
	if nm.begins_with("WorkLight"): return 16.0
	if nm == "Camera_ScanBeam": return 30.0
	if nm.begins_with("Backroom") or nm.begins_with("ObsRoom"): return 12.0
	if nm.contains("Emergency"): return 6.0
	if nm.begins_with("Crystal"): return 6.0
	if nm.begins_with("Lantern"): return 10.0
	return 8.0

func _in_foliage(n: Node) -> bool:
	var p := n
	while p != null:
		for f in FOLIAGE:
			if String(p.name).begins_with(f):
				return true
		p = p.get_parent()
	return false

func _setup_visuals(n: Node) -> void:
	if n is Light3D:
		var l := n as Light3D
		var nm := String(l.name)
		# Dynamic: renders in real time now, and is still included if a LightmapGI bake is added later.
		l.light_bake_mode = Light3D.BAKE_DYNAMIC
		if l is DirectionalLight3D:
			l.light_energy *= SUN_ENERGY_SCALE
			l.shadow_enabled = true
		elif l is SpotLight3D:
			l.light_energy *= SPOT_ENERGY_SCALE
			(l as SpotLight3D).spot_range = _light_range(nm)
			l.shadow_enabled = l.light_energy >= SHADOW_ENERGY_MIN
		elif l is OmniLight3D:
			l.light_energy *= OMNI_ENERGY_SCALE
			(l as OmniLight3D).omni_range = _light_range(nm)
			l.shadow_enabled = l.light_energy >= SHADOW_ENERGY_MIN
	elif n is MeshInstance3D:
		var mi := n as MeshInstance3D
		var foliage := _in_foliage(mi)
		if foliage:
			mi.gi_mode = GeometryInstance3D.GI_MODE_DYNAMIC
		if mi.mesh:
			if mi.mesh.resource_name == "Lantern":
				mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF   # its light sits inside it
			for s in mi.mesh.get_surface_count():
				var mat := mi.mesh.surface_get_material(s)
				if not (mat is StandardMaterial3D):
					continue
				var sm := mat as StandardMaterial3D
				if mi.mesh.surface_get_format(s) & Mesh.ARRAY_FORMAT_COLOR:
					sm.vertex_color_use_as_albedo = true   # panel grime / rock strata are vertex colours
				if NO_SHADOW_MATERIALS.has(sm.resource_name):
					mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
				if foliage:
					sm.cull_mode = BaseMaterial3D.CULL_DISABLED
	for c in n.get_children():
		_setup_visuals(c)
