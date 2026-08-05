@tool
extends SceneTree

const RADIO_SCENE := "res://scenes/interactables/new_office/NewRadioStation.tscn"
const MAP_SCENE := "res://scenes/interactables/new_office/NewWallMap.tscn"
const NOTEBOOK_SCENE := "res://scenes/interactables/new_office/NewNotebook.tscn"


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var succeeded := _upgrade_radio() and _upgrade_map() and _upgrade_notebook()
	if succeeded:
		print("New office radio, wall map and notebook upgraded to functional scenes.")
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


func _add_outline(root: Node) -> void:
	var outline := Node.new()
	outline.name = "InteractionOutline"
	outline.set_script(load("res://scripts/interaction/InteractionOutline.cs"))
	outline.set("VisualRootPath", NodePath("../VisualRoot"))
	_owned_child(root, outline, root)


func _add_interaction_area(root: Node, label: String, size: Vector3, position: Vector3 = Vector3.ZERO) -> void:
	var area := Area3D.new()
	area.name = "InteractionArea"
	area.position = position
	area.collision_layer = 2
	area.collision_mask = 0
	area.set_script(load("res://scripts/interaction/OutlineOnlyInteractable.cs"))
	area.set("Label", label)
	_owned_child(root, area, root)
	var collision := CollisionShape3D.new()
	collision.name = "CollisionShape3D"
	var shape := BoxShape3D.new()
	shape.size = size
	collision.shape = shape
	_owned_child(area, collision, root)


func _upgrade_radio() -> bool:
	var loaded := _load_scene(RADIO_SCENE)
	if loaded.is_empty():
		return false
	var packed: PackedScene = loaded[0]
	var root: Node3D = loaded[1]
	if root.has_node("InteractionArea"):
		root.free()
		return true

	root.set_script(load("res://scripts/radio/DeskRadio.cs"))
	root.add_to_group("interactive", true)
	var signal_light := OmniLight3D.new()
	signal_light.name = "SignalLight"
	signal_light.position = Vector3(-0.08, 0.32, -0.14)
	signal_light.light_color = Color(0.38, 1.0, 0.48)
	signal_light.light_energy = 1.4
	signal_light.omni_range = 0.75
	signal_light.visible = false
	_owned_child(root.get_node("VisualRoot"), signal_light, root)

	_add_outline(root)
	var area := Area3D.new()
	area.name = "InteractionArea"
	area.position = Vector3(-0.08, 0.0, -0.12)
	area.collision_layer = 2
	area.collision_mask = 0
	area.set_script(load("res://scripts/radio/DeskRadioInteractable.cs"))
	_owned_child(root, area, root)
	var collision := CollisionShape3D.new()
	collision.name = "CollisionShape3D"
	var shape := BoxShape3D.new()
	shape.size = Vector3(0.75, 0.72, 0.72)
	collision.shape = shape
	_owned_child(area, collision, root)
	return _save_scene(RADIO_SCENE, packed, root)


func _upgrade_map() -> bool:
	var loaded := _load_scene(MAP_SCENE)
	if loaded.is_empty():
		return false
	var packed: PackedScene = loaded[0]
	var root: Node3D = loaded[1]
	if root.has_node("MapViewport"):
		root.free()
		return true

	root.add_to_group("interactive", true)
	var visual_root := root.get_node("VisualRoot")
	var viewport_surface := MeshInstance3D.new()
	viewport_surface.name = "ViewportSurface"
	viewport_surface.position = Vector3(0.036, 0.0, 0.0)
	viewport_surface.rotation = Vector3(0.0, PI * 0.5, 0.0)
	var viewport_quad := QuadMesh.new()
	viewport_quad.size = Vector2(1.30, 0.93)
	viewport_surface.mesh = viewport_quad
	_owned_child(visual_root, viewport_surface, root)

	var background_surface := MeshInstance3D.new()
	background_surface.name = "MapBackgroundSurface"
	background_surface.transform = viewport_surface.transform
	var background_quad := QuadMesh.new()
	background_quad.size = viewport_quad.size
	var background_material := StandardMaterial3D.new()
	background_material.albedo_texture = load("res://assets/textures/test man.png")
	background_material.roughness = 0.9
	background_quad.material = background_material
	background_surface.mesh = background_quad
	_owned_child(visual_root, background_surface, root)
	var marker_container := Node3D.new()
	marker_container.name = "MapMarkers"
	_owned_child(visual_root, marker_container, root)

	_add_outline(root)
	_add_interaction_area(root, "Map", Vector3(0.10, 1.04, 1.46))

	var viewport := SubViewport.new()
	viewport.name = "MapViewport"
	viewport.disable_3d = true
	viewport.transparent_bg = true
	viewport.size = Vector2i(1024, 768)
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	_owned_child(root, viewport, root)
	var map_ui_scene := load("res://scenes/ui/map/MapUI.tscn") as PackedScene
	var map_ui := map_ui_scene.instantiate()
	map_ui.name = "MapUI"
	_owned_child(viewport, map_ui, root)

	var renderer := Node.new()
	renderer.name = "MapSurfaceRenderer"
	renderer.set_script(load("res://scripts/interaction/SubViewportSurfaceRenderer.cs"))
	renderer.set("SurfacePath", NodePath("../VisualRoot/ViewportSurface"))
	renderer.set("ViewportPath", NodePath("../MapViewport"))
	_owned_child(root, renderer, root)
	var marker_controller := Node.new()
	marker_controller.name = "MapMarkerController"
	marker_controller.set_script(load("res://scripts/interactables/map_marker/MapMarkerController.cs"))
	marker_controller.set("MapMissionMarkerScene", load("res://scenes/interactables/map_marker/MapMissionMarker.tscn"))
	marker_controller.add_to_group("map_marker_controller", true)
	_owned_child(root, marker_controller, root)
	var background_layout := Node.new()
	background_layout.name = "MapBackgroundLayout"
	background_layout.set_script(load("res://scripts/interactables/MapBackgroundLayout.cs"))
	_owned_child(root, background_layout, root)
	return _save_scene(MAP_SCENE, packed, root)


func _upgrade_notebook() -> bool:
	var loaded := _load_scene(NOTEBOOK_SCENE)
	if loaded.is_empty():
		return false
	var packed: PackedScene = loaded[0]
	var root: Node3D = loaded[1]
	if root.has_node("NotebookViewport"):
		root.free()
		return true

	root.add_to_group("interactive", true)
	var viewport_surface := MeshInstance3D.new()
	viewport_surface.name = "ViewportSurface"
	viewport_surface.position = Vector3(0.0, 0.013, 0.0)
	var surface_mesh := PlaneMesh.new()
	surface_mesh.size = Vector2(0.18, 0.13)
	viewport_surface.mesh = surface_mesh
	_owned_child(root.get_node("VisualRoot"), viewport_surface, root)
	_add_outline(root)
	_add_interaction_area(root, "Notebook", Vector3(0.23, 0.06, 0.18))

	var viewport := SubViewport.new()
	viewport.name = "NotebookViewport"
	viewport.disable_3d = true
	viewport.size = Vector2i(700, 990)
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	_owned_child(root, viewport, root)
	var page_background := ColorRect.new()
	page_background.name = "PageBackground"
	page_background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	page_background.mouse_filter = Control.MOUSE_FILTER_IGNORE
	page_background.color = Color(0.045, 0.065, 0.055)
	_owned_child(viewport, page_background, root)
	var notebook_ui_scene := load("res://scenes/ui/notebook/NotebookUI.tscn") as PackedScene
	var notebook_ui := notebook_ui_scene.instantiate()
	notebook_ui.name = "NotebookUI"
	_owned_child(viewport, notebook_ui, root)
	var renderer := Node.new()
	renderer.name = "NotebookSurfaceRenderer"
	renderer.set_script(load("res://scripts/interaction/SubViewportSurfaceRenderer.cs"))
	renderer.set("SurfacePath", NodePath("../VisualRoot/ViewportSurface"))
	renderer.set("ViewportPath", NodePath("../NotebookViewport"))
	_owned_child(root, renderer, root)
	return _save_scene(NOTEBOOK_SCENE, packed, root)
