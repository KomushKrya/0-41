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
	var shared := main.get_node_or_null("SharedSystems")
	if old_office == null or new_office == null or shared == null:
		push_error("Main scene must contain SharedSystems, OldOffice and NewOffice.")
		main.free()
		quit(1)
		return

	_rename_if_present(new_office, "RadioStationVisual", "RadioStation")
	_rename_if_present(new_office, "WallMapVisual", "WallMap")
	_rename_if_present(new_office, "NotebookVisual", "Notebook")

	_remove_if_present(old_office, "WallMap")
	_remove_if_present(old_office, "Desk/RadioStation")
	_remove_if_present(old_office, "Desk/Notebook")

	var dossier := old_office.get_node_or_null("Desk/EmployeeDossierFolder")
	if dossier != null:
		_move_scene_node(dossier, new_office, main)
		dossier.transform = Transform3D(Basis(Vector3.UP, -0.2), Vector3(1.72, 0.79, 1.93))
	var display_pose := old_office.get_node_or_null("Desk/DossierDisplayPose")
	if display_pose != null:
		_move_scene_node(display_pose, new_office, main)
		display_pose.transform = Transform3D(
			Basis(Vector3.RIGHT, -1.05),
			Vector3(1.93, 1.12, 1.98)
		)
	var shift_note := old_office.get_node_or_null("Desk/ShiftNote")
	if shift_note != null:
		_move_scene_node(shift_note, new_office, main)
		shift_note.transform = Transform3D(Basis(Vector3.UP, 0.16), Vector3(1.16, 0.79, 1.92))

	var kontur_debug := old_office.get_node_or_null("KonturDebug")
	if kontur_debug != null:
		_move_scene_node(kontur_debug, shared, main)

	var debug_overlay := shared.get_node_or_null("DebugInterfaceOverlay")
	if debug_overlay != null:
		debug_overlay.set("MapViewportPath", NodePath("../../NewOffice/WallMap/MapViewport"))
		debug_overlay.set("DossierViewportPath", NodePath("../../NewOffice/EmployeeDossierFolder/DossierViewport"))
		debug_overlay.set("NotebookViewportPath", NodePath("../../NewOffice/Notebook/NotebookViewport"))

	old_office.set("visible", false)
	old_office.process_mode = Node.PROCESS_MODE_DISABLED

	var required_paths := [
		"SharedSystems/KonturDebug",
		"NewOffice/RadioStation/InteractionArea",
		"NewOffice/WallMap/MapViewport",
		"NewOffice/WallMap/MapMarkerController",
		"NewOffice/Notebook/NotebookViewport",
		"NewOffice/EmployeeDossierFolder/DossierViewport",
		"NewOffice/DossierDisplayPose",
		"NewOffice/ShiftNote",
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
	print("Main scene migrated through final new office phase.")
	quit()


func _move_scene_node(child: Node, destination: Node, scene_root: Node) -> void:
	child.owner = null
	child.reparent(destination, false)
	child.owner = scene_root


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
