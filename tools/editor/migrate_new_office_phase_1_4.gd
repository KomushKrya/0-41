@tool
extends SceneTree

const MAIN_SCENE := "res://scenes/main.tscn"


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var packed := ResourceLoader.load(MAIN_SCENE, "PackedScene", ResourceLoader.CACHE_MODE_REPLACE) as PackedScene
	if packed == null:
		push_error("Cannot load main scene: %s" % MAIN_SCENE)
		quit(1)
		return
	var main := packed.instantiate(PackedScene.GEN_EDIT_STATE_MAIN)
	var old_office := main.get_node_or_null("OldOffice")
	var new_office := main.get_node_or_null("NewOffice")
	if old_office == null or new_office == null:
		push_error("Main scene must contain OldOffice and NewOffice.")
		main.free()
		quit(1)
		return

	var shared := main.get_node_or_null("SharedSystems")
	if shared == null:
		shared = Node.new()
		shared.name = "SharedSystems"
		main.add_child(shared)
		shared.owner = main
		main.move_child(shared, 0)

	_move_if_present(old_office, "ScreenSpaceOutlineManager", shared)
	_move_if_present(old_office, "DebugInterfaceOverlay", shared)
	_move_if_present(old_office, "RadioDecisionLayer", shared)
	_move_if_present(old_office, "PhoneCallAcceptanceLayer", shared)

	var phone_ui := shared.get_node_or_null("PhoneCallAcceptanceLayer/PhoneCallAcceptanceUI")
	if phone_ui != null:
		phone_ui.add_to_group("phone_call_acceptance_ui", true)
	var radio_ui := shared.get_node_or_null("RadioDecisionLayer/RadioDecisionUI")
	if radio_ui != null:
		radio_ui.add_to_group("radio_decision_ui", true)

	_rename_if_present(new_office, "OfficeChairVisual", "OfficeChair")
	_rename_if_present(new_office, "DeskComputerVisual", "DeskComputer")
	_rename_if_present(new_office, "DeskPhoneVisual", "DeskPhone")
	var player := old_office.get_node_or_null("Player")
	if player != null:
		player.reparent(new_office, true)
		player.owner = main
	player = new_office.get_node_or_null("Player")
	if player != null:
		player.set("InitialSeatPosePath", NodePath("../OfficeChair/FocusCameraPose"))
		player.add_to_group("player", true)

	_remove_if_present(old_office, "Desk/DeskComputer")
	_remove_if_present(old_office, "Desk/DeskPhone")

	var debug_overlay := shared.get_node_or_null("DebugInterfaceOverlay")
	if debug_overlay != null:
		debug_overlay.set("PlayerPath", NodePath("../../NewOffice/Player"))
		debug_overlay.set("InteractionRayPath", NodePath("../../NewOffice/Player/Head/Camera3D/InteractionRay"))
		debug_overlay.set("PcViewportPath", NodePath("../../NewOffice/DeskComputer/ComputerViewport"))
		debug_overlay.set("MapViewportPath", NodePath("../../OldOffice/WallMap/MapViewport"))
		debug_overlay.set("DossierViewportPath", NodePath("../../OldOffice/Desk/EmployeeDossierFolder/DossierViewport"))
		debug_overlay.set("NotebookViewportPath", NodePath("../../OldOffice/Desk/Notebook/NotebookViewport"))
	var outline_manager := shared.get_node_or_null("ScreenSpaceOutlineManager")
	if outline_manager != null:
		outline_manager.set("SourceCameraPath", NodePath("../../NewOffice/Player/Head/Camera3D"))

	var required_paths := [
		"SharedSystems/ScreenSpaceOutlineManager",
		"SharedSystems/PhoneCallAcceptanceLayer/PhoneCallAcceptanceUI",
		"NewOffice/OfficeChair/FocusCameraPose",
		"NewOffice/Player",
		"NewOffice/DeskComputer/ComputerViewport",
		"NewOffice/DeskComputer/InteractionArea",
		"NewOffice/DeskPhone/InteractionArea",
	]
	for path: String in required_paths:
		if not main.has_node(path):
			push_error("Migrated main scene is missing: %s" % path)
			main.free()
			quit(1)
			return

	var pack_error := packed.pack(main)
	if pack_error != OK:
		push_error("Cannot repack main scene: %s" % error_string(pack_error))
		main.free()
		quit(1)
		return
	var save_error := ResourceSaver.save(packed, MAIN_SCENE, ResourceSaver.FLAG_RELATIVE_PATHS)
	main.free()
	if save_error != OK:
		push_error("Cannot save main scene: %s" % error_string(save_error))
		quit(1)
		return
	print("Main scene migrated through new office phone/computer phase.")
	quit()


func _move_if_present(source: Node, child_path: String, destination: Node) -> void:
	var child := source.get_node_or_null(child_path)
	if child == null:
		return
	var scene_owner := child.owner
	child.owner = null
	child.reparent(destination)
	child.owner = scene_owner


func _rename_if_present(parent: Node, old_name: String, new_name: String) -> void:
	var child := parent.get_node_or_null(old_name)
	if child != null:
		child.name = new_name


func _remove_if_present(parent: Node, child_path: String) -> void:
	var child := parent.get_node_or_null(child_path)
	if child == null:
		return
	child.get_parent().remove_child(child)
	child.free()
