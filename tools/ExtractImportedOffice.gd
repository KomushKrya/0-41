extends SceneTree

const SOURCE_PATH := "res://assets/models/environment/import/кабинет.glb"
const TARGET_PATH := "res://scenes/environment/ImportedOffice.tscn"

const NODE_NAMES := {
	"Cube": "WallMapBoard",
	"Cube_001": "DeskNotebook",
	"Cube_002": "DoorPanel",
	"Cube_002_low": "DeskKeyboard",
	"Cube_006": "OfficeChairFrame",
	"Cube_007": "OfficeChairSeat",
	"Cylinder": "DeskPencil",
	"Cylinder_001": "DoorHandle",
	"Cylinder_002": "DoorHandleWoodGrip",
	"Cylinder_003": "DeskAccessory",
	"Cylinder_014": "DeskRadio",
	"microphone_accessories_0": "RadioMicrophone",
	"Object_8": "CoatRackHat",
	"Phone_LowPoly_004": "DeskPhone",
	"Plane": "ComputerMonitorScreen",
	"polySurface313__0": "DeskMouse",
	"Torus_008": "ComputerBase",
	"Torus_015": "DeskComputer",
	"батарея": "DecorativeBattery",
	"большая стрелка": "ClockHourHand",
	"вешалка": "CoatRack",
	"диванчик": "Sofa",
	"Лампа": "DeskLamp",
	"маленькая стрелка": "ClockMinuteHand",
	"окно": "Window",
	"окурок": "AshtrayCigarette",
	"пепел": "AshtrayAsh",
	"пепельница": "Ashtray",
	"Плоскость": "FloorDecoration",
	"подстаканик": "CupCoaster",
	"пол": "FloorSurface",
	"потолок": "CeilingSurface",
	"рамка": "WallPictureFrame",
	"рамка_001": "DeskPictureFrame",
	"ручка": "WindowHandle",
	"стакан": "DrinkingGlass",
	"стена 1": "WallSection01",
	"стена 2": "WallSection02",
	"стена 3": "WallSection03",
	"стена 4": "WallSection04",
	"стол": "Desk",
	"фикус": "Ficus",
	"часы": "WallClock",
	"шкаф": "CabinetLeft",
	"шкаф_001": "CabinetRight",
}


func _init() -> void:
	call_deferred("_extract")


func _extract() -> void:
	var source := load(SOURCE_PATH) as PackedScene
	if source == null:
		push_error("Unable to load imported office: %s" % SOURCE_PATH)
		quit(1)
		return

	var source_root := source.instantiate()
	var root := Node3D.new()
	root.name = "ImportedOffice"
	source_root.name = "ImportedOfficeGeometry"
	root.add_child(source_root)
	_make_children_local(root, root)

	var renamed := 0
	for original_name in NODE_NAMES:
		var node := root.find_child(original_name, true, true)
		if node == null:
			push_error("Imported office node was not found: %s" % original_name)
			quit(1)
			return
		node.name = NODE_NAMES[original_name]
		renamed += 1

	if renamed != NODE_NAMES.size():
		push_error("Expected to rename %d office objects, renamed %d." % [NODE_NAMES.size(), renamed])
		quit(1)
		return

	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(TARGET_PATH.get_base_dir()))
	var packed_scene := PackedScene.new()
	var pack_error := packed_scene.pack(root)
	if pack_error != OK:
		push_error("Unable to pack imported office: %s" % error_string(pack_error))
		quit(1)
		return

	var save_error := ResourceSaver.save(packed_scene, TARGET_PATH)
	if save_error != OK:
		push_error("Unable to save unpacked office: %s" % error_string(save_error))
		quit(1)
		return

	print("Unpacked imported office saved to %s; renamed %d objects." % [TARGET_PATH, renamed])
	quit(0)


func _make_children_local(node: Node, owner: Node) -> void:
	for child in node.get_children():
		child.owner = owner
		_make_children_local(child, owner)
