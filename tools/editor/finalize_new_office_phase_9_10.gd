@tool
extends SceneTree

const MAIN_SCENE := "res://scenes/main.tscn"

const COLLISION_MESH_PATHS := [
	"RoomShell/FloorBase",
	"RoomShell/Ceiling",
	"RoomShell/Wall01",
	"RoomShell/Wall02",
	"RoomShell/Wall03",
	"RoomShell/Wall04",
	"Desk",
	"Sofa",
	"CabinetLeft",
	"CabinetRight",
	"Radiator",
	"Ficus",
	"CoatRack",
]

const TINY_SHADOW_MESH_PATHS := [
	"DoorHandleMetal",
	"DoorHandleWood",
	"ClockHourHand",
	"ClockMinuteHand",
	"CigaretteButt",
	"Ash",
	"WindowHandle",
	"DeskAccessory01",
]


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var packed := ResourceLoader.load(MAIN_SCENE, "PackedScene", ResourceLoader.CACHE_MODE_REPLACE) as PackedScene
	if packed == null:
		push_error("Cannot load main scene: %s" % MAIN_SCENE)
		quit(1)
		return

	var main := packed.instantiate(PackedScene.GEN_EDIT_STATE_MAIN)
	var old_office := main.get_node_or_null("OldOffice") as Node3D
	var new_office := main.get_node_or_null("NewOffice") as Node3D
	if old_office == null or new_office == null:
		push_error("Main scene must contain OldOffice and NewOffice.")
		main.free()
		quit(1)
		return

	# Final switch: the replacement office occupies the canonical game-space origin.
	new_office.transform = Transform3D.IDENTITY
	new_office.remove_from_group("visual_only")
	old_office.visible = false
	old_office.process_mode = Node.PROCESS_MODE_DISABLED

	_replace_environment_systems(new_office, main)
	_disable_tiny_object_shadows(new_office)

	if not _validate(main):
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

	print("New office environment and final switch completed.")
	quit()


func _replace_environment_systems(new_office: Node3D, scene_root: Node) -> void:
	var existing := new_office.get_node_or_null("EnvironmentSystems")
	if existing != null:
		new_office.remove_child(existing)
		existing.free()

	var systems := Node3D.new()
	systems.name = "EnvironmentSystems"
	_owned_child(new_office, systems, scene_root)

	var world_environment := WorldEnvironment.new()
	world_environment.name = "WorldEnvironment"
	var environment := Environment.new()
	environment.background_mode = Environment.BG_COLOR
	environment.background_color = Color("11151c")
	environment.background_energy_multiplier = 0.35
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.ambient_light_color = Color("8fa0b4")
	environment.ambient_light_energy = 0.38
	environment.reflected_light_source = Environment.REFLECTION_SOURCE_BG
	environment.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	world_environment.environment = environment
	_owned_child(systems, world_environment, scene_root)

	var ceiling_light := OmniLight3D.new()
	ceiling_light.name = "CeilingLight"
	ceiling_light.position = Vector3(1.2, 2.28, 1.3)
	ceiling_light.light_color = Color("ffd4a3")
	ceiling_light.light_energy = 1.65
	ceiling_light.omni_range = 5.2
	ceiling_light.omni_attenuation = 1.35
	ceiling_light.shadow_enabled = true
	ceiling_light.shadow_bias = 0.08
	_owned_child(systems, ceiling_light, scene_root)

	var desk_light := SpotLight3D.new()
	desk_light.name = "DeskLight"
	desk_light.position = Vector3(1.1, 1.38, 1.5)
	desk_light.rotation_degrees = Vector3(-67.0, -18.0, 0.0)
	desk_light.light_color = Color("ffc680")
	desk_light.light_energy = 1.1
	desk_light.spot_range = 2.7
	desk_light.spot_angle = 42.0
	desk_light.spot_attenuation = 1.4
	desk_light.shadow_enabled = false
	_owned_child(systems, desk_light, scene_root)

	var static_body := StaticBody3D.new()
	static_body.name = "EnvironmentCollision"
	static_body.collision_layer = 1
	static_body.collision_mask = 1
	_owned_child(systems, static_body, scene_root)

	for mesh_path: String in COLLISION_MESH_PATHS:
		var mesh_instance := new_office.get_node_or_null(mesh_path) as MeshInstance3D
		if mesh_instance == null or mesh_instance.mesh == null:
			push_warning("Cannot build collision for missing mesh: %s" % mesh_path)
			continue
		_add_box_collision(static_body, mesh_instance, new_office, scene_root)


func _add_box_collision(
	static_body: StaticBody3D,
	mesh_instance: MeshInstance3D,
	new_office: Node3D,
	scene_root: Node
) -> void:
	var aabb := mesh_instance.mesh.get_aabb()
	var size := aabb.size
	size.x = maxf(size.x, 0.06)
	size.y = maxf(size.y, 0.06)
	size.z = maxf(size.z, 0.06)

	var shape := BoxShape3D.new()
	shape.size = size
	var collision := CollisionShape3D.new()
	collision.name = "%sCollision" % mesh_instance.name
	collision.shape = shape
	var mesh_to_office := _transform_to_ancestor(mesh_instance, new_office)
	collision.transform = mesh_to_office * Transform3D(Basis.IDENTITY, aabb.get_center())
	_owned_child(static_body, collision, scene_root)


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


func _disable_tiny_object_shadows(new_office: Node3D) -> void:
	for mesh_path: String in TINY_SHADOW_MESH_PATHS:
		var mesh_instance := new_office.get_node_or_null(mesh_path) as MeshInstance3D
		if mesh_instance != null:
			mesh_instance.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF


func _owned_child(parent: Node, child: Node, scene_root: Node) -> void:
	parent.add_child(child)
	child.owner = scene_root


func _validate(main: Node) -> bool:
	var new_office := main.get_node("NewOffice") as Node3D
	var old_office := main.get_node("OldOffice") as Node3D
	if not new_office.position.is_zero_approx():
		push_error("NewOffice must be at the world origin.")
		return false
	if old_office.visible or old_office.process_mode != Node.PROCESS_MODE_DISABLED:
		push_error("OldOffice must be hidden with processing disabled.")
		return false

	var required_paths := [
		"NewOffice/EnvironmentSystems/WorldEnvironment",
		"NewOffice/EnvironmentSystems/CeilingLight",
		"NewOffice/EnvironmentSystems/DeskLight",
		"NewOffice/EnvironmentSystems/EnvironmentCollision",
		"NewOffice/Player/Head/Camera3D/InteractionRay",
		"NewOffice/DeskComputer/ComputerViewport",
		"NewOffice/DeskPhone/InteractionArea",
		"NewOffice/RadioStation/InteractionArea",
		"NewOffice/WallMap/MapMarkerController",
		"NewOffice/Notebook/NotebookViewport",
		"NewOffice/EmployeeDossierFolder/DossierViewport",
		"NewOffice/ShiftNote",
	]
	for path: String in required_paths:
		if not main.has_node(path):
			push_error("Final main scene is missing: %s" % path)
			return false

	var collision_root := main.get_node("NewOffice/EnvironmentSystems/EnvironmentCollision")
	if collision_root.get_child_count() != COLLISION_MESH_PATHS.size():
		push_error(
			"Expected %s environment collisions, found %s."
			% [COLLISION_MESH_PATHS.size(), collision_root.get_child_count()]
		)
		return false
	return true
