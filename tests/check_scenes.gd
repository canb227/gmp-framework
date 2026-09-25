# Headless check that every .tscn/.tres under res://game and res://gmp loads.
# Run: Godot_console.exe --headless --path <project> --script res://tests/check_scenes.gd
extends SceneTree

var failures := 0
var checked := 0

func _init() -> void:
	for root in ["res://game", "res://gmp"]:
		_walk(root)
	print("CHECK_SCENES checked=%d failures=%d" % [checked, failures])
	print("CHECK_SCENES_RESULT:%s" % ("PASS" if failures == 0 else "FAIL"))
	quit(0 if failures == 0 else 1)

func _walk(path: String) -> void:
	var dir := DirAccess.open(path)
	if dir == null:
		return
	for sub in dir.get_directories():
		_walk(path.path_join(sub))
	for f in dir.get_files():
		if f.ends_with(".tscn") or f.ends_with(".tres"):
			var p := path.path_join(f)
			checked += 1
			var res = ResourceLoader.load(p)
			if res == null:
				failures += 1
				print("CHECK_SCENES_FAIL ", p)
			elif res is PackedScene and not res.can_instantiate():
				failures += 1
				print("CHECK_SCENES_FAIL (cannot instantiate) ", p)
