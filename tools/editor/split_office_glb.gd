@tool
extends SceneTree

const SOURCE_GLB := "res://assets/models/environment/import/кабинет.glb"
const EDITABLE_OFFICE_SCENE := "res://scenes/environment/NewOffice.tscn"
const OUTPUT_ROOT := "res://assets/models/environment/office_split"

const OBJECT_CATEGORIES := {
	"FloorBase": "architecture",
	"FloorFinish": "architecture",
	"Ceiling": "architecture",
	"Wall01": "architecture",
	"Wall02": "architecture",
	"Wall03": "architecture",
	"Wall04": "architecture",
	"Door": "architecture",
	"DoorHandleMetal": "architecture",
	"DoorHandleWood": "architecture",
	"Window": "architecture",
	"WindowHandle": "architecture",
	"Radiator": "architecture",
	"Desk": "furniture",
	"Sofa": "furniture",
	"CabinetLeft": "furniture",
	"CabinetRight": "furniture",
	"OfficeChairFrame": "furniture",
	"OfficeChairSeat": "furniture",
	"CoatRack": "furniture",
	"WallMapBoard": "interactive",
	"Notebook": "interactive",
	"Pencil": "interactive",
	"DeskPhone": "interactive",
	"Radio": "interactive",
	"RadioMicrophoneAccessories": "interactive",
	"DeskAccessory02": "interactive",
	"Keyboard": "interactive",
	"ComputerMonitor": "interactive",
	"ComputerCase": "interactive",
	"ComputerControls": "interactive",
	"DeskLamp": "lighting",
	"DeskAccessory01": "decor",
	"FedoraHat": "decor",
	"ClockHourHand": "decor",
	"ClockMinuteHand": "decor",
	"Clock": "decor",
	"CigaretteButt": "decor",
	"Ash": "decor",
	"Ashtray": "decor",
	"CupHolder": "decor",
	"WallFrame01": "decor",
	"WallFrame02": "decor",
	"DrinkingGlass": "decor",
	"Ficus": "decor",
}


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var packed := ResourceLoader.load(
		EDITABLE_OFFICE_SCENE,
		"PackedScene",
		ResourceLoader.CACHE_MODE_REPLACE
	) as PackedScene
	if packed == null:
		push_error("Cannot load editable office scene: %s" % EDITABLE_OFFICE_SCENE)
		quit(1)
		return

	var office := packed.instantiate()
	if office == null:
		push_error("Cannot instantiate editable office scene.")
		quit(1)
		return

	if not _prepare_directories():
		office.free()
		quit(1)
		return

	var manifest_objects: Array[Dictionary] = []
	var exported_count := 0
	var total_bytes := 0

	for object_name: String in OBJECT_CATEGORIES:
		var source := office.find_child(object_name, true, false) as MeshInstance3D
		if source == null or source.mesh == null:
			push_error("Office mesh not found: %s" % object_name)
			office.free()
			quit(1)
			return

		var category: String = OBJECT_CATEGORIES[object_name]
		var file_name := object_name.to_snake_case() + ".glb"
		var output_path := "%s/%s/%s" % [OUTPUT_ROOT, category, file_name]
		var transform_to_office := _transform_to_ancestor(source, office as Node3D)
		var dimensions := _transformed_aabb(source.mesh.get_aabb(), transform_to_office.basis).size

		var export_error := _export_object(source, object_name, transform_to_office.basis, output_path)
		if export_error != OK:
			push_error("Cannot export %s: %s" % [object_name, error_string(export_error)])
			office.free()
			quit(1)
			return

		var byte_size := _file_size(output_path)
		total_bytes += byte_size
		exported_count += 1
		manifest_objects.append({
			"name": object_name,
			"category": category,
			"file": output_path.trim_prefix("res://"),
			"source_node_path": str(office.get_path_to(source)),
			"dimensions_m": _vector_to_array(dimensions),
			"original_transform": _transform_to_dictionary(transform_to_office),
			"mesh_local_aabb": _aabb_to_dictionary(source.mesh.get_aabb()),
			"surface_count": source.mesh.get_surface_count(),
			"file_size_bytes": byte_size,
		})
		print("Exported %s/%s: %s" % [exported_count, OBJECT_CATEGORIES.size(), output_path])

	office.free()
	manifest_objects.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
		if a["category"] == b["category"]:
			return a["name"] < b["name"]
		return a["category"] < b["category"]
	)

	var manifest := {
		"format_version": 1,
		"source_glb": SOURCE_GLB.trim_prefix("res://"),
		"coordinate_system": "Godot 4, meters, Y-up; each GLB origin is rebased while basis and dimensions are preserved",
		"object_count": exported_count,
		"total_file_size_bytes": total_bytes,
		"categories": ["architecture", "furniture", "interactive", "lighting", "decor"],
		"objects": manifest_objects,
	}
	var manifest_path := OUTPUT_ROOT + "/manifest.json"
	var manifest_file := FileAccess.open(manifest_path, FileAccess.WRITE)
	if manifest_file == null:
		push_error("Cannot write manifest: %s" % manifest_path)
		quit(1)
		return
	manifest_file.store_string(JSON.stringify(manifest, "  ", false) + "\n")
	manifest_file.close()

	print("Office split complete: %s GLB files, %s bytes." % [exported_count, total_bytes])
	quit()


func _prepare_directories() -> bool:
	for category: String in ["architecture", "furniture", "interactive", "lighting", "decor"]:
		var absolute_path := ProjectSettings.globalize_path("%s/%s" % [OUTPUT_ROOT, category])
		var error := DirAccess.make_dir_recursive_absolute(absolute_path)
		if error != OK:
			push_error("Cannot create output directory %s: %s" % [absolute_path, error_string(error)])
			return false
	return true


func _export_object(
	source: MeshInstance3D,
	object_name: String,
	original_basis: Basis,
	output_path: String
) -> Error:
	var export_root := Node3D.new()
	export_root.name = object_name
	var mesh_copy := MeshInstance3D.new()
	mesh_copy.name = object_name + "Mesh"
	mesh_copy.mesh = source.mesh
	mesh_copy.material_override = source.material_override
	mesh_copy.material_overlay = source.material_overlay
	mesh_copy.cast_shadow = source.cast_shadow
	mesh_copy.transform = Transform3D(original_basis, Vector3.ZERO)
	export_root.add_child(mesh_copy)
	mesh_copy.owner = export_root

	var document := GLTFDocument.new()
	var state := GLTFState.new()
	var append_error := document.append_from_scene(export_root, state)
	if append_error != OK:
		export_root.free()
		return append_error
	var write_error := document.write_to_filesystem(state, output_path)
	export_root.free()
	return write_error


func _transform_to_ancestor(node: Node3D, ancestor: Node3D) -> Transform3D:
	var result := node.transform
	var parent := node.get_parent()
	while parent != ancestor:
		if not parent is Node3D:
			push_error("Non-3D parent between %s and %s." % [node.name, ancestor.name])
			return result
		result = (parent as Node3D).transform * result
		parent = parent.get_parent()
	return result


func _transformed_aabb(aabb: AABB, basis: Basis) -> AABB:
	var corners := [
		aabb.position,
		aabb.position + Vector3(aabb.size.x, 0.0, 0.0),
		aabb.position + Vector3(0.0, aabb.size.y, 0.0),
		aabb.position + Vector3(0.0, 0.0, aabb.size.z),
		aabb.position + Vector3(aabb.size.x, aabb.size.y, 0.0),
		aabb.position + Vector3(aabb.size.x, 0.0, aabb.size.z),
		aabb.position + Vector3(0.0, aabb.size.y, aabb.size.z),
		aabb.end,
	]
	var first: Vector3 = basis * corners[0]
	var result := AABB(first, Vector3.ZERO)
	for index in range(1, corners.size()):
		result = result.expand(basis * corners[index])
	return result


func _vector_to_array(value: Vector3) -> Array[float]:
	return [value.x, value.y, value.z]


func _basis_to_array(value: Basis) -> Array:
	return [
		_vector_to_array(value.x),
		_vector_to_array(value.y),
		_vector_to_array(value.z),
	]


func _transform_to_dictionary(value: Transform3D) -> Dictionary:
	return {
		"basis_columns": _basis_to_array(value.basis),
		"origin": _vector_to_array(value.origin),
	}


func _aabb_to_dictionary(value: AABB) -> Dictionary:
	return {
		"position": _vector_to_array(value.position),
		"size": _vector_to_array(value.size),
	}


func _file_size(path: String) -> int:
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		return 0
	var length := file.get_length()
	file.close()
	return length
