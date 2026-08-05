@tool
extends SceneTree

const CHAIR_SCENE := "res://scenes/interactables/new_office/NewOfficeChair.tscn"
const COMPUTER_SCENE := "res://scenes/interactables/new_office/NewDeskComputer.tscn"
const PHONE_SCENE := "res://scenes/interactables/new_office/NewDeskPhone.tscn"


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var succeeded := _upgrade_chair() and _upgrade_computer() and _upgrade_phone()
	if succeeded:
		print("New office chair, computer and phone upgraded to functional scenes.")
	quit(0 if succeeded else 1)


func _load_scene(path: String) -> Array:
	var packed := ResourceLoader.load(path, "PackedScene", ResourceLoader.CACHE_MODE_REPLACE) as PackedScene
	if packed == null:
		push_error("Cannot load scene: %s" % path)
		return []
	var root := packed.instantiate(PackedScene.GEN_EDIT_STATE_MAIN)
	if root == null:
		push_error("Cannot instantiate scene: %s" % path)
		return []
	return [packed, root]


func _save_scene(path: String, packed: PackedScene, root: Node) -> bool:
	var pack_error := packed.pack(root)
	if pack_error != OK:
		push_error("Cannot pack %s: %s" % [path, error_string(pack_error)])
		root.free()
		return false
	var save_error := ResourceSaver.save(packed, path, ResourceSaver.FLAG_RELATIVE_PATHS)
	root.free()
	if save_error != OK:
		push_error("Cannot save %s: %s" % [path, error_string(save_error)])
		return false
	return true


func _owned_child(parent: Node, child: Node, scene_root: Node) -> Node:
	parent.add_child(child)
	child.owner = scene_root
	return child


func _upgrade_chair() -> bool:
	var loaded := _load_scene(CHAIR_SCENE)
	if loaded.is_empty():
		return false
	var packed: PackedScene = loaded[0]
	var root: Node3D = loaded[1]
	if root.has_node("FocusCameraPose"):
		root.free()
		return true

	root.add_to_group("seat_anchor", true)
	var focus_pose := Camera3D.new()
	focus_pose.name = "FocusCameraPose"
	focus_pose.transform = Transform3D(
		Basis.looking_at(Vector3(-0.99, -0.14, -0.08).normalized(), Vector3.UP),
		Vector3(0.28, 1.24, 0.0)
	)
	_owned_child(root, focus_pose, root)

	var body := StaticBody3D.new()
	body.name = "StaticBody3D"
	_owned_child(root, body, root)
	var collision := CollisionShape3D.new()
	collision.name = "CollisionShape3D"
	collision.position = Vector3(0.25, 0.48, 0.0)
	var shape := BoxShape3D.new()
	shape.size = Vector3(0.72, 0.96, 0.72)
	collision.shape = shape
	_owned_child(body, collision, root)
	return _save_scene(CHAIR_SCENE, packed, root)


func _upgrade_computer() -> bool:
	var loaded := _load_scene(COMPUTER_SCENE)
	if loaded.is_empty():
		return false
	var packed: PackedScene = loaded[0]
	var root: Node3D = loaded[1]
	if root.has_node("ComputerViewport"):
		root.free()
		return true

	root.set_script(load("res://scripts/computer/DeskComputerInteraction.cs"))
	root.add_to_group("desk_computer", true)
	root.add_to_group("interactive", true)

	var viewport := SubViewport.new()
	viewport.name = "ComputerViewport"
	viewport.disable_3d = true
	viewport.size = Vector2i(900, 600)
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	_owned_child(root, viewport, root)
	var computer_ui_scene := load("res://scenes/ui/computer/ComputerUI.tscn") as PackedScene
	var computer_ui := computer_ui_scene.instantiate()
	computer_ui.name = "ComputerUI"
	_owned_child(viewport, computer_ui, root)

	var interaction_area := Area3D.new()
	interaction_area.name = "InteractionArea"
	interaction_area.collision_layer = 2
	interaction_area.collision_mask = 0
	interaction_area.set_script(load("res://scripts/computer/DeskComputerInteractable.cs"))
	interaction_area.set("ComputerPath", NodePath(".."))
	interaction_area.set("OutlinePath", NodePath("../InteractionOutline"))
	_owned_child(root, interaction_area, root)
	var collision := CollisionShape3D.new()
	collision.name = "CollisionShape3D"
	collision.position = Vector3(0.05, 0.0, 0.12)
	var shape := BoxShape3D.new()
	shape.size = Vector3(0.85, 0.75, 0.72)
	collision.shape = shape
	_owned_child(interaction_area, collision, root)

	var focus_pose := Camera3D.new()
	focus_pose.name = "FocusCameraPose"
	var focus_origin := Vector3(-0.48, 0.26, 0.45)
	var monitor_target := Vector3(0.015, -0.024, 0.13)
	focus_pose.transform = Transform3D(
		Basis.looking_at((monitor_target - focus_origin).normalized(), Vector3.UP),
		focus_origin
	)
	_owned_child(root, focus_pose, root)
	var dossier_pose := Camera3D.new()
	dossier_pose.name = "DossierFocusCameraPose"
	dossier_pose.position = Vector3(0.0, -0.02, 0.18)
	_owned_child(focus_pose, dossier_pose, root)

	var screen_renderer := Node3D.new()
	screen_renderer.name = "ScreenRenderer"
	screen_renderer.set_script(load("res://scripts/computer/ComputerScreenRenderer.cs"))
	screen_renderer.set("ScreenPath", NodePath("VisualRoot/ComputerMonitor"))
	screen_renderer.set("ScreenNodeName", "ComputerMonitor")
	_owned_child(root, screen_renderer, root)

	var viewport_input := Node.new()
	viewport_input.name = "ViewportInput"
	viewport_input.set_script(load("res://scripts/interaction/SubViewportInputController.cs"))
	viewport_input.set("ViewportPath", NodePath("../ComputerViewport"))
	viewport_input.set("CursorPath", NodePath("../ComputerViewport/ComputerUI/ComputerCursor"))
	viewport_input.set("CursorBoundsPath", NodePath("../ComputerViewport/ComputerUI/SafeArea"))
	_owned_child(root, viewport_input, root)
	var dossier_input := Node.new()
	dossier_input.name = "DossierViewportInput"
	dossier_input.set_script(load("res://scripts/interaction/SubViewportInputController.cs"))
	_owned_child(root, dossier_input, root)

	var outline := Node.new()
	outline.name = "InteractionOutline"
	outline.set_script(load("res://scripts/interaction/InteractionOutline.cs"))
	outline.set("VisualRootPath", NodePath("../VisualRoot"))
	_owned_child(root, outline, root)
	return _save_scene(COMPUTER_SCENE, packed, root)


func _upgrade_phone() -> bool:
	var loaded := _load_scene(PHONE_SCENE)
	if loaded.is_empty():
		return false
	var packed: PackedScene = loaded[0]
	var root: Node3D = loaded[1]
	if root.has_node("InteractionArea"):
		root.free()
		return true

	root.set_script(load("res://scripts/phone/DeskPhone.cs"))
	root.add_to_group("interactive", true)
	var visual_root := root.get_node("VisualRoot")
	var ring_light := OmniLight3D.new()
	ring_light.name = "RingLight"
	ring_light.position = Vector3(0.0, 0.24, 0.0)
	ring_light.light_color = Color(1.0, 0.12, 0.04)
	ring_light.light_energy = 2.2
	ring_light.omni_range = 0.85
	ring_light.visible = false
	_owned_child(visual_root, ring_light, root)

	var interaction_area := Area3D.new()
	interaction_area.name = "InteractionArea"
	interaction_area.position = Vector3(0.0, 0.13, 0.0)
	interaction_area.collision_layer = 2
	interaction_area.collision_mask = 0
	interaction_area.set_script(load("res://scripts/phone/DeskPhoneInteractable.cs"))
	_owned_child(root, interaction_area, root)
	var collision := CollisionShape3D.new()
	collision.name = "CollisionShape3D"
	var shape := BoxShape3D.new()
	shape.size = Vector3(0.55, 0.32, 0.52)
	collision.shape = shape
	_owned_child(interaction_area, collision, root)

	var outline := Node.new()
	outline.name = "InteractionOutline"
	outline.set_script(load("res://scripts/interaction/InteractionOutline.cs"))
	outline.set("VisualRootPath", NodePath("../VisualRoot"))
	_owned_child(root, outline, root)
	return _save_scene(PHONE_SCENE, packed, root)
