@tool
extends SceneTree

const FILES := [
	"res://temp/портрет ленина.glb",
	"res://temp/Фотография семьи.glb",
	"res://temp/радио.glb",
	"res://temp/лампа патолочная.glb",
]

func _initialize() -> void:
	for path in FILES:
		print("==== ", path, " ====")
		var abs_path := ProjectSettings.globalize_path(path)
		var document := GLTFDocument.new()
		var state := GLTFState.new()
		var err := document.append_from_file(abs_path, state)
		if err != OK:
			print("  FAILED TO LOAD: ", error_string(err))
			continue
		var root := document.generate_scene(state)
		if root == null:
			print("  generate_scene returned null")
			continue
		_dump(root, 1)
		root.free()
	quit()

func _dump(node: Node, depth: int) -> void:
	var indent := "  ".repeat(depth)
	var extra := ""
	if node is MeshInstance3D:
		var mi := node as MeshInstance3D
		if mi.mesh:
			extra = " aabb=%s surfaces=%d" % [mi.mesh.get_aabb(), mi.mesh.get_surface_count()]
	if node is Node3D:
		var n3 := node as Node3D
		extra += " pos=%s rot=%s scale=%s" % [n3.position, n3.rotation, n3.scale]
	print(indent, node.name, " [", node.get_class(), "]", extra)
	for child in node.get_children():
		_dump(child, depth + 1)
