extends SceneTree

## Запускает маршрут группы на настенной карте и снимает доску в лоб, чтобы
## оценить след пунктира. Маршрут строится штатным StartDispatchRoute от штаба
## до указанного здания, положение головы задаётся через UpdateDispatchRoute.

const OUTPUT_PATH := "res://temp/map_geometry/map_route.png"
const WALL_MAP_PATH := "/root/Main/NewOffice/Interactable/WallMap"
const INCIDENT_ID := "route_probe"
const BUILDING_ID := "building_block_r02_c01"
const TRAVEL_SECONDS := 20.0
## Сколько времени «осталось»: чем меньше, тем дальше уехала голова следа.
const REMAINING_SECONDS := 7.0
const WARMUP_FRAMES := 200

const FACE_CENTRE := Vector3(2.8734, 1.4907, 2.0701)
const CAMERA_DISTANCE := 1.05
const CAMERA_FOV := 45.0

var _camera: Camera3D = null
var _route_started := false
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

	if not _route_started:
		_start_route()

	# Голову держим на месте: MapMarkerController гонит по маршрутам собственные
	# обновления из core, и без этого след уполз бы за время прогрева.
	if _route_started:
		_map_ui().call("UpdateDispatchRoute", INCIDENT_ID, REMAINING_SECONDS)

	if _frames < WARMUP_FRAMES:
		return false

	var image := get_root().get_texture().get_image()
	print("saved %s %s" % [OUTPUT_PATH, image.save_png(OUTPUT_PATH)])
	return true

func _map_ui() -> Control:
	var wall_map := get_root().get_node_or_null(WALL_MAP_PATH)
	return null if wall_map == null else wall_map.get_node_or_null("MapViewport/MapUI") as Control

func _start_route() -> void:
	var map_ui := _map_ui()
	if map_ui == null:
		return

	var started: bool = map_ui.call("StartDispatchRoute", INCIDENT_ID, BUILDING_ID, TRAVEL_SECONDS)
	if not started:
		push_error("Маршрут до '%s' построить не удалось." % BUILDING_ID)
		return

	print("маршрут запущен до %s" % BUILDING_ID)
	_route_started = true
