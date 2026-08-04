@tool
extends SceneTree

const SOURCE_SCENE := "res://assets/models/environment/import/кабинет.glb"
const OUTPUT_SCENE := "res://scenes/environment/NewOffice.tscn"
const INTERACTIVE_SCENE_DIRECTORY := "res://scenes/interactables/new_office"
const ROOM_SHELL_NODES := [
	"FloorBase",
	"FloorFinish",
	"Ceiling",
	"Wall01",
	"Wall02",
	"Wall03",
	"Wall04",
]

const INTERACTIVE_OBJECTS := [
	{
		"scene_path": INTERACTIVE_SCENE_DIRECTORY + "/NewOfficeChair.tscn",
		"root_name": "OfficeChair",
		"instance_name": "OfficeChairVisual",
		"anchor": "OfficeChairFrame",
		"nodes": ["OfficeChairFrame", "OfficeChairSeat"],
	},
	{
		"scene_path": INTERACTIVE_SCENE_DIRECTORY + "/NewWallMap.tscn",
		"root_name": "WallMap",
		"instance_name": "WallMapVisual",
		"anchor": "WallMapBoard",
		"nodes": ["WallMapBoard"],
	},
	{
		"scene_path": INTERACTIVE_SCENE_DIRECTORY + "/NewNotebook.tscn",
		"root_name": "Notebook",
		"instance_name": "NotebookVisual",
		"anchor": "Notebook",
		"nodes": ["Notebook", "Pencil"],
	},
	{
		"scene_path": INTERACTIVE_SCENE_DIRECTORY + "/NewDeskPhone.tscn",
		"root_name": "DeskPhone",
		"instance_name": "DeskPhoneVisual",
		"anchor": "DeskPhone",
		"nodes": ["DeskPhone"],
	},
	{
		"scene_path": INTERACTIVE_SCENE_DIRECTORY + "/NewRadioStation.tscn",
		"root_name": "RadioStation",
		"instance_name": "RadioStationVisual",
		"anchor": "Radio",
		"nodes": ["Radio", "RadioMicrophoneAccessories", "DeskAccessory02"],
	},
	{
		"scene_path": INTERACTIVE_SCENE_DIRECTORY + "/NewDeskComputer.tscn",
		"root_name": "DeskComputer",
		"instance_name": "DeskComputerVisual",
		"anchor": "ComputerControls",
		"nodes": ["Keyboard", "ComputerMonitor", "ComputerCase", "ComputerControls"],
	},
]

const NODE_RENAMES := {
	"Cube": "WallMapBoard",
	"Cube.001": "Notebook",
	"Cube_001": "Notebook",
	"Cube.002": "Door",
	"Cube_002": "Door",
	"Cube.002_low": "Keyboard",
	"Cube_002_low": "Keyboard",
	"Cube.006": "OfficeChairFrame",
	"Cube_006": "OfficeChairFrame",
	"Cube.007": "OfficeChairSeat",
	"Cube_007": "OfficeChairSeat",
	"Cylinder": "Pencil",
	"Cylinder.001": "DoorHandleMetal",
	"Cylinder_001": "DoorHandleMetal",
	"Cylinder.002": "DoorHandleWood",
	"Cylinder_002": "DoorHandleWood",
	"Cylinder.003": "DeskAccessory01",
	"Cylinder_003": "DeskAccessory01",
	"Cylinder.014": "Radio",
	"Cylinder_014": "Radio",
	"microphone_accessories_0": "RadioMicrophoneAccessories",
	"Object_8": "FedoraHat",
	"Phone_LowPoly.004": "DeskPhone",
	"Phone_LowPoly_004": "DeskPhone",
	"Plane": "ComputerMonitor",
	"polySurface313__0": "DeskAccessory02",
	"Torus.008": "ComputerCase",
	"Torus_008": "ComputerCase",
	"Torus.015": "ComputerControls",
	"Torus_015": "ComputerControls",
	"батарея": "Radiator",
	"большая стрелка": "ClockHourHand",
	"вешалка": "CoatRack",
	"диванчик": "Sofa",
	"Лампа": "DeskLamp",
	"маленькая стрелка": "ClockMinuteHand",
	"окно": "Window",
	"окурок": "CigaretteButt",
	"пепел": "Ash",
	"пепельница": "Ashtray",
	"Плоскость": "FloorBase",
	"подстаканик": "CupHolder",
	"пол": "FloorFinish",
	"потолок": "Ceiling",
	"рамка": "WallFrame01",
	"рамка.001": "WallFrame02",
	"рамка_001": "WallFrame02",
	"ручка": "WindowHandle",
	"стакан": "DrinkingGlass",
	"стена 1": "Wall01",
	"стена 2": "Wall02",
	"стена 3": "Wall03",
	"стена 4": "Wall04",
	"стол": "Desk",
	"фикус": "Ficus",
	"часы": "Clock",
	"шкаф": "CabinetLeft",
	"шкаф.001": "CabinetRight",
	"шкаф_001": "CabinetRight",
}


func _initialize() -> void:
	call_deferred("_unpack_scene")


func _unpack_scene() -> void:
	var imported := load(SOURCE_SCENE) as PackedScene
	if imported == null:
		push_error("Cannot load imported office: %s" % SOURCE_SCENE)
		quit(1)
		return

	var office := imported.instantiate(PackedScene.GEN_EDIT_STATE_INSTANCE)
	if office == null:
		push_error("Cannot instantiate imported office: %s" % SOURCE_SCENE)
		quit(1)
		return

	office.name = "NewOffice"
	var renamed_count := _rename_nodes(office)
	var extract_error := _extract_interactive_objects(office)
	if extract_error != OK:
		office.free()
		quit(1)
		return
	var room_shell_error := _group_room_shell(office)
	if room_shell_error != OK:
		office.free()
		quit(1)
		return
	_assign_owner(office, office)

	var packed := PackedScene.new()
	var pack_error := packed.pack(office)
	if pack_error != OK:
		push_error("Cannot pack new office scene: %s" % error_string(pack_error))
		office.free()
		quit(1)
		return

	var output_directory := OUTPUT_SCENE.get_base_dir()
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(output_directory))
	var save_error := ResourceSaver.save(packed, OUTPUT_SCENE, ResourceSaver.FLAG_RELATIVE_PATHS)
	office.free()

	if save_error != OK:
		push_error("Cannot save new office scene: %s" % error_string(save_error))
		quit(1)
		return

	print(
		"New office unpacked: %s nodes renamed, %s interactive scenes extracted, saved to %s"
		% [renamed_count, INTERACTIVE_OBJECTS.size(), OUTPUT_SCENE]
	)
	quit()


func _rename_nodes(node: Node) -> int:
	var renamed_count := 0
	for child in node.get_children():
		var old_name := String(child.name)
		if NODE_RENAMES.has(old_name):
			child.name = NODE_RENAMES[old_name]
			renamed_count += 1
		renamed_count += _rename_nodes(child)
	return renamed_count


func _assign_owner(node: Node, scene_root: Node) -> void:
	for child in node.get_children():
		child.owner = scene_root
		if child.scene_file_path.is_empty():
			_assign_owner(child, scene_root)


func _extract_interactive_objects(office: Node) -> Error:
	DirAccess.make_dir_recursive_absolute(
		ProjectSettings.globalize_path(INTERACTIVE_SCENE_DIRECTORY)
	)

	for definition in INTERACTIVE_OBJECTS:
		var extract_error := _extract_interactive_object(office, definition)
		if extract_error != OK:
			return extract_error
	return OK


func _group_room_shell(office: Node) -> Error:
	var room_shell := Node3D.new()
	room_shell.name = "RoomShell"
	office.add_child(room_shell)
	room_shell.owner = office

	for node_name: String in ROOM_SHELL_NODES:
		var mesh_node := office.find_child(node_name, true, false) as Node3D
		if mesh_node == null:
			push_error("Cannot find room-shell mesh node: %s" % node_name)
			room_shell.queue_free()
			return ERR_DOES_NOT_EXIST

		var local_transform := mesh_node.transform
		mesh_node.owner = null
		mesh_node.get_parent().remove_child(mesh_node)
		room_shell.add_child(mesh_node)
		mesh_node.transform = local_transform
		mesh_node.owner = office

	return OK


func _extract_interactive_object(office: Node, definition: Dictionary) -> Error:
	var extracted_nodes: Array[Node3D] = []
	for node_name: String in definition["nodes"]:
		var mesh_node := office.find_child(node_name, true, false) as Node3D
		if mesh_node == null:
			push_error("Cannot find new-office mesh node: %s" % node_name)
			return ERR_DOES_NOT_EXIST
		extracted_nodes.append(mesh_node)

	var anchor := office.find_child(definition["anchor"], true, false) as Node3D
	if anchor == null:
		push_error("Cannot find anchor node: %s" % definition["anchor"])
		return ERR_DOES_NOT_EXIST
	var anchor_transform := anchor.transform
	var inverse_anchor := anchor_transform.affine_inverse()

	var scene_root := Node3D.new()
	scene_root.name = definition["root_name"]
	var visual_root := Node3D.new()
	visual_root.name = "VisualRoot"
	scene_root.add_child(visual_root)
	visual_root.owner = scene_root

	for mesh_node in extracted_nodes:
		var office_transform := mesh_node.transform
		mesh_node.owner = null
		mesh_node.get_parent().remove_child(mesh_node)
		visual_root.add_child(mesh_node)
		mesh_node.transform = inverse_anchor * office_transform
		mesh_node.owner = scene_root
		_make_mesh_resources_unique(mesh_node)
		_assign_owner(mesh_node, scene_root)

	var packed := PackedScene.new()
	var pack_error := packed.pack(scene_root)
	if pack_error != OK:
		push_error(
			"Cannot pack interactive scene %s: %s"
			% [definition["scene_path"], error_string(pack_error)]
		)
		scene_root.free()
		return pack_error

	var save_error := ResourceSaver.save(
		packed,
		definition["scene_path"],
		ResourceSaver.FLAG_RELATIVE_PATHS
	)
	if save_error != OK:
		push_error(
			"Cannot save interactive scene %s: %s"
			% [definition["scene_path"], error_string(save_error)]
		)
		scene_root.free()
		return save_error

	var saved_scene := ResourceLoader.load(
		definition["scene_path"],
		"PackedScene",
		ResourceLoader.CACHE_MODE_REPLACE
	) as PackedScene
	if saved_scene == null:
		push_error("Cannot reload extracted scene: %s" % definition["scene_path"])
		scene_root.free()
		return ERR_FILE_CANT_READ

	var instance := saved_scene.instantiate() as Node3D
	if instance == null:
		push_error("Cannot instantiate extracted scene: %s" % definition["scene_path"])
		scene_root.free()
		return ERR_CANT_CREATE
	instance.name = definition["instance_name"]
	instance.transform = anchor_transform
	office.add_child(instance)
	instance.owner = office
	scene_root.free()
	return OK


func _make_mesh_resources_unique(node: Node) -> void:
	if node is MeshInstance3D:
		var mesh_instance := node as MeshInstance3D
		if mesh_instance.mesh != null:
			var source_mesh := mesh_instance.mesh
			var local_mesh := source_mesh.duplicate(false) as Mesh
			if local_mesh != null:
				for surface_index in range(source_mesh.get_surface_count()):
					var source_material := source_mesh.surface_get_material(surface_index)
					if source_material != null:
						local_mesh.surface_set_material(
							surface_index,
							source_material.duplicate(false)
						)
				mesh_instance.mesh = local_mesh
		if mesh_instance.material_override != null:
			mesh_instance.material_override = mesh_instance.material_override.duplicate(false)

	for child in node.get_children():
		_make_mesh_resources_unique(child)
