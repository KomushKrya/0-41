extends SceneTree

## Ставит маркеры заданий на случайные здания карты и переводит каждый в своё
## состояние кольца, чтобы одним снимком увидеть всю палитру индикаторов.
##
## Здания берутся прямо из BuildingLayer, а перевод в точку на доске повторяет
## MapMarkerController.CreatePin: центр полигона -> доля от размера MapUI ->
## локальные координаты квада поверхности.

const OUTPUT_PATH := "res://temp/map_geometry/map_marker_states.png"
const MARKER_SCENE := "res://scenes/interactables/map_marker/MapMissionMarker.tscn"
const WALL_MAP_PATH := "/root/Main/NewOffice/Interactable/WallMap"
const SURFACE_OFFSET := 0.003
const RANDOM_SEED := 41
const WARMUP_FRAMES := 200

const FACE_CENTRE := Vector3(2.8734, 1.4907, 2.0701)
const CAMERA_DISTANCE := 1.25
const CAMERA_FOV := 40.0

## Состояния кольца: метод индикатора и его аргументы.
const STATES := [
	{"name": "отсчёт до отправки, 75%", "state": 1, "args": [15.0, 20.0]},
	{"name": "отсчёт до отправки, 30%", "state": 1, "args": [6.0, 20.0]},
	{"name": "группа в пути", "state": 2, "args": [1.0, 1.0]},
	{"name": "ожидание рации, 60%", "state": 3, "args": [12.0, 20.0]},
	{"name": "работа на объекте, 45%", "state": 4, "args": [9.0, 20.0]},
	{"name": "индикатор скрыт", "state": 0, "args": [0.0, 0.0]},
]

var _camera: Camera3D = null
var _markers_placed := false
var _frames := 0

func _initialize() -> void:
	get_root().add_child((load("res://scenes/main.tscn") as PackedScene).instantiate())

	_camera = Camera3D.new()
	_camera.fov = CAMERA_FOV
	_camera.current = true
	_camera.transform = Transform3D(
		Vector3(0, 0, 1), Vector3(0, 1, 0), Vector3(-1, 0, 0),
		FACE_CENTRE - Vector3(CAMERA_DISTANCE, 0, 0))
	get_root().add_child(_camera)

func _process(_delta: float) -> bool:
	_frames += 1
	_camera.current = true

	if not _markers_placed:
		_place_markers()

	if _frames < WARMUP_FRAMES:
		return false

	var image := get_root().get_texture().get_image()
	print("saved %s %s" % [OUTPUT_PATH, image.save_png(OUTPUT_PATH)])
	return true

func _place_markers() -> void:
	var wall_map := get_root().get_node_or_null(WALL_MAP_PATH)
	if wall_map == null:
		return

	var surface := wall_map.get_node_or_null("VisualRoot/ViewportSurface") as MeshInstance3D
	var markers := wall_map.get_node_or_null("VisualRoot/MapMarkers") as Node3D
	var map_ui := wall_map.get_node_or_null("MapViewport/MapUI") as Control
	if surface == null or markers == null or map_ui == null:
		return

	var building_layer := map_ui.get_node_or_null("MapLayers/BuildingLayer") as Control
	if building_layer == null or not (surface.mesh is QuadMesh):
		push_error("Не найден BuildingLayer или поверхность не QuadMesh.")
		return

	var buildings: Array[Polygon2D] = []
	for child in building_layer.get_children():
		if child is Polygon2D and (child as Polygon2D).polygon.size() >= 3:
			buildings.append(child)

	if buildings.is_empty():
		push_error("В BuildingLayer нет полигонов.")
		return

	var random := RandomNumberGenerator.new()
	random.seed = RANDOM_SEED
	var used: Dictionary = {}
	var scene := load(MARKER_SCENE) as PackedScene

	for state in STATES:
		var building := _pick_building(buildings, used, random)
		var centre := _building_centre(building, map_ui)
		var marker := scene.instantiate()
		markers.add_child(marker)
		marker.transform = Transform3D(
			surface.basis.orthonormalized(),
			surface.transform * _to_surface_point(centre, map_ui, surface.mesh as QuadMesh))
		marker.call("Initialize", "state_probe_%s" % state["name"])
		marker.callv("ShowRing", [state["state"]] + state["args"])
		print("%-24s -> %s" % [state["name"], building.name])

	_markers_placed = true

func _pick_building(buildings: Array[Polygon2D], used: Dictionary, random: RandomNumberGenerator) -> Polygon2D:
	for _attempt in range(64):
		var index := random.randi_range(0, buildings.size() - 1)
		if not used.has(index):
			used[index] = true
			return buildings[index]

	return buildings[0]

## Центр полигона в системе координат MapUI.
func _building_centre(building: Polygon2D, map_ui: Control) -> Vector2:
	var to_map := map_ui.get_global_transform().affine_inverse() * building.get_global_transform()
	var centre := Vector2.ZERO
	for point in building.polygon:
		centre += to_map * point

	return centre / float(building.polygon.size())

## Точка на квадре поверхности: доля от размера MapUI, потом локальные координаты меша.
func _to_surface_point(centre: Vector2, map_ui: Control, quad: QuadMesh) -> Vector3:
	var normalized := Vector2(centre.x / map_ui.size.x, centre.y / map_ui.size.y)
	return Vector3(
		(normalized.x - 0.5) * quad.size.x,
		(0.5 - normalized.y) * quad.size.y,
		SURFACE_OFFSET)
