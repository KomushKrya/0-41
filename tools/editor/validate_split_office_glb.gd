@tool
extends SceneTree

const MANIFEST_PATH := "res://assets/models/environment/office_split/manifest.json"
const DIMENSION_TOLERANCE_METERS := 0.001


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var manifest_text := FileAccess.get_file_as_string(MANIFEST_PATH)
	var manifest = JSON.parse_string(manifest_text)
	if not manifest is Dictionary:
		push_error("Cannot parse split-office manifest.")
		quit(1)
		return

	var objects: Array = manifest.get("objects", [])
	if objects.size() != 45:
		push_error("Expected 45 manifest objects, found %s." % objects.size())
		quit(1)
		return

	var checked := 0
	for entry_variant in objects:
		var entry := entry_variant as Dictionary
		var path := "res://" + str(entry["file"])
		if not FileAccess.file_exists(path):
			push_error("Split GLB is missing: %s" % path)
			quit(1)
			return

		var document := GLTFDocument.new()
		var state := GLTFState.new()
		var append_error := document.append_from_file(path, state)
		if append_error != OK:
			push_error("Cannot parse %s: %s" % [path, error_string(append_error)])
			quit(1)
			return
		var generated := document.generate_scene(state)
		if generated == null:
			push_error("Cannot generate validation scene from %s." % path)
			quit(1)
			return

		var measured := _combined_mesh_aabb(generated)
		var expected_values: Array = entry["dimensions_m"]
		var expected := Vector3(
			float(expected_values[0]),
			float(expected_values[1]),
			float(expected_values[2])
		)
		if not measured.size.is_equal_approx(expected):
			var difference := (measured.size - expected).abs()
			if difference.x > DIMENSION_TOLERANCE_METERS \
				or difference.y > DIMENSION_TOLERANCE_METERS \
				or difference.z > DIMENSION_TOLERANCE_METERS:
				push_error(
					"Dimension mismatch for %s: expected %s, measured %s."
					% [entry["name"], expected, measured.size]
				)
				generated.free()
				quit(1)
				return

		generated.free()
		checked += 1

	print("Validated %s split GLB files; dimensions match within %s m." % [checked, DIMENSION_TOLERANCE_METERS])
	quit()


func _combined_mesh_aabb(root: Node) -> AABB:
	var meshes: Array[MeshInstance3D] = []
	_collect_meshes(root, meshes)
	if meshes.is_empty():
		return AABB()

	var result := _mesh_aabb_in_root(meshes[0], root)
	for index in range(1, meshes.size()):
		result = result.merge(_mesh_aabb_in_root(meshes[index], root))
	return result


func _collect_meshes(node: Node, output: Array[MeshInstance3D]) -> void:
	if node is MeshInstance3D and (node as MeshInstance3D).mesh != null:
		output.append(node as MeshInstance3D)
	for child in node.get_children():
		_collect_meshes(child, output)


func _mesh_aabb_in_root(mesh_instance: MeshInstance3D, root: Node) -> AABB:
	var transform := mesh_instance.transform
	var parent := mesh_instance.get_parent()
	while parent != root:
		if parent is Node3D:
			transform = (parent as Node3D).transform * transform
		parent = parent.get_parent()

	var local_aabb := mesh_instance.mesh.get_aabb()
	var corners := [
		local_aabb.position,
		local_aabb.position + Vector3(local_aabb.size.x, 0.0, 0.0),
		local_aabb.position + Vector3(0.0, local_aabb.size.y, 0.0),
		local_aabb.position + Vector3(0.0, 0.0, local_aabb.size.z),
		local_aabb.position + Vector3(local_aabb.size.x, local_aabb.size.y, 0.0),
		local_aabb.position + Vector3(local_aabb.size.x, 0.0, local_aabb.size.z),
		local_aabb.position + Vector3(0.0, local_aabb.size.y, local_aabb.size.z),
		local_aabb.end,
	]
	var result := AABB(transform * corners[0], Vector3.ZERO)
	for index in range(1, corners.size()):
		result = result.expand(transform * corners[index])
	return result
