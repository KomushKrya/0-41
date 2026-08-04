@tool
extends SceneTree

const MAIN_SCENE := "res://scenes/main.tscn"


func _initialize() -> void:
	call_deferred("_embed_new_office")


func _embed_new_office() -> void:
	var packed := load(MAIN_SCENE) as PackedScene
	if packed == null:
		push_error("Cannot load main scene: %s" % MAIN_SCENE)
		quit(1)
		return

	var main := packed.instantiate(PackedScene.GEN_EDIT_STATE_MAIN)
	if main == null:
		push_error("Cannot instantiate main scene: %s" % MAIN_SCENE)
		quit(1)
		return

	var source_office := main.get_node_or_null("NewOffice") as Node3D
	if source_office == null:
		push_error("Main scene does not contain NewOffice.")
		main.free()
		quit(1)
		return

	if source_office.scene_file_path.is_empty():
		if _validate_embedded_office(source_office):
			print("NewOffice is already embedded in main.tscn.")
			main.free()
			quit()
		else:
			main.free()
			quit(1)
		return

	var office_transform := source_office.transform
	var source_children := source_office.get_children()
	var source_groups := source_office.get_groups()
	source_office.name = "NewOfficeSource"

	var local_office := Node3D.new()
	local_office.name = "NewOffice"
	local_office.transform = office_transform
	main.add_child(local_office)
	local_office.owner = main
	for group_name: StringName in source_groups:
		local_office.add_to_group(group_name, true)

	for child in source_children:
		child.owner = null
		source_office.remove_child(child)
		local_office.add_child(child)
		child.owner = main
		_assign_main_owner(child, main)

	main.remove_child(source_office)
	source_office.free()

	if not _validate_embedded_office(local_office):
		main.free()
		quit(1)
		return

	var pack_error := packed.pack(main)
	if pack_error != OK:
		push_error("Cannot repack main scene: %s" % error_string(pack_error))
		main.free()
		quit(1)
		return

	var save_error := ResourceSaver.save(
		packed,
		MAIN_SCENE,
		ResourceSaver.FLAG_RELATIVE_PATHS
	)
	main.free()
	if save_error != OK:
		push_error("Cannot save embedded main scene: %s" % error_string(save_error))
		quit(1)
		return

	print(
		"NewOffice embedded into main.tscn with %s direct child nodes."
		% source_children.size()
	)
	quit()


func _assign_main_owner(node: Node, main: Node) -> void:
	if not node.scene_file_path.is_empty():
		return
	for child in node.get_children():
		child.owner = main
		_assign_main_owner(child, main)


func _validate_embedded_office(office: Node) -> bool:
	var required_paths := [
		"RoomShell/FloorBase",
		"RoomShell/FloorFinish",
		"RoomShell/Ceiling",
		"RoomShell/Wall01",
		"RoomShell/Wall02",
		"RoomShell/Wall03",
		"RoomShell/Wall04",
	]
	for required_path: String in required_paths:
		if not office.has_node(required_path):
			push_error("Embedded NewOffice is missing: %s" % required_path)
			return false
	var interactive_name_pairs := [
		["OfficeChair", "OfficeChairVisual"],
		["WallMap", "WallMapVisual"],
		["Notebook", "NotebookVisual"],
		["DeskPhone", "DeskPhoneVisual"],
		["RadioStation", "RadioStationVisual"],
		["DeskComputer", "DeskComputerVisual"],
	]
	for names: Array in interactive_name_pairs:
		if not office.has_node(names[0]) and not office.has_node(names[1]):
			push_error("Embedded NewOffice is missing: %s or %s" % names)
			return false
	return true
