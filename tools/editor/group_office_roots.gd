@tool
extends SceneTree

const MAIN_SCENE := "res://scenes/main.tscn"


func _initialize() -> void:
	call_deferred("_group_offices")


func _group_offices() -> void:
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

	if main.has_node("OldOffice") and main.has_node("NewOffice"):
		var required_new_office_paths := [
			"NewOffice/RoomShell/FloorBase",
			"NewOffice/RoomShell/FloorFinish",
			"NewOffice/RoomShell/Ceiling",
			"NewOffice/RoomShell/Wall01",
			"NewOffice/RoomShell/Wall02",
			"NewOffice/RoomShell/Wall03",
			"NewOffice/RoomShell/Wall04",
		]
		for required_path: String in required_new_office_paths:
			if not main.has_node(required_path):
				push_error("Grouped main scene is missing: %s" % required_path)
				main.free()
				quit(1)
				return
		print("Main scene already contains valid OldOffice and NewOffice roots.")
		main.free()
		quit()
		return
	if main.has_node("OldOffice") or main.has_node("NewOffice"):
		push_error("Main scene contains only one office root; refusing a partial regroup.")
		main.free()
		quit(1)
		return

	main.name = "Main"
	var original_children := main.get_children()

	var old_office := Node3D.new()
	old_office.name = "OldOffice"
	main.add_child(old_office)
	old_office.owner = main

	var moved_to_old := 0
	var moved_to_new := 0
	for child in original_children:
		var local_transform := Transform3D.IDENTITY
		if child is Node3D:
			local_transform = (child as Node3D).transform

		child.owner = null
		main.remove_child(child)
		if child.name == "ImportedOfficePreview":
			child.name = "NewOffice"
			main.add_child(child)
			if child is Node3D:
				(child as Node3D).transform = local_transform
			moved_to_new += 1
		else:
			old_office.add_child(child)
			if child is Node3D:
				(child as Node3D).transform = local_transform
			moved_to_old += 1
		child.owner = main

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
		push_error("Cannot save grouped main scene: %s" % error_string(save_error))
		quit(1)
		return

	print(
		"Office roots created: %s nodes moved to OldOffice, %s node moved to NewOffice."
		% [moved_to_old, moved_to_new]
	)
	quit()
