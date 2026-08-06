extends SceneTree

## Ставит тестовый маркер задания в центр настенной карты в main.tscn и снимает
## доску в лоб — чтобы оценить размер кнопки и кольца. Маркеры в игре появляются
## только когда core заводит инцидент, поэтому для проверки размера ставим свой.

const OUTPUT_PATH := "res://temp/map_geometry/map_marker_size.png"
const MARKER_SCENE := "res://scenes/interactables/map_marker/MapMissionMarker.tscn"
const WALL_MAP_PATH := "/root/Main/NewOffice/Interactable/WallMap"
const WARMUP_FRAMES := 200

## Лицо доски и отступ камеры от него, как в _shot_wall_map.gd.
const FACE_CENTRE := Vector3(2.8734, 1.4907, 2.0701)
const CAMERA_DISTANCE := 1.0

var _camera: Camera3D = null
var _marker_placed := false
var _frames := 0

func _initialize() -> void:
	get_root().add_child((load("res://scenes/main.tscn") as PackedScene).instantiate())

	_camera = Camera3D.new()
	_camera.fov = 40.0
	_camera.current = true
	_camera.transform = Transform3D(
		Vector3(0, 0, 1), Vector3(0, 1, 0), Vector3(-1, 0, 0),
		FACE_CENTRE - Vector3(CAMERA_DISTANCE, 0, 0))
	get_root().add_child(_camera)

func _process(_delta: float) -> bool:
	_frames += 1
	_camera.current = true

	if not _marker_placed:
		_place_marker()

	if _frames < WARMUP_FRAMES:
		return false

	var image := get_root().get_texture().get_image()
	print("saved %s %s" % [OUTPUT_PATH, image.save_png(OUTPUT_PATH)])
	return true

func _place_marker() -> void:
	var wall_map := get_root().get_node_or_null(WALL_MAP_PATH)
	if wall_map == null:
		return

	var surface := wall_map.get_node_or_null("VisualRoot/ViewportSurface") as MeshInstance3D
	var markers := wall_map.get_node_or_null("VisualRoot/MapMarkers") as Node3D
	if surface == null or markers == null:
		return

	var marker := (load(MARKER_SCENE) as PackedScene).instantiate()
	markers.add_child(marker)
	marker.transform = Transform3D(
		surface.basis.orthonormalized(),
		surface.transform * Vector3(0.0, 0.0, 0.003))
	marker.call("Initialize", "size_probe")
	marker.call("ShowDispatchCountdown", 20.0, 20.0)
	_marker_placed = true
	print("маркер поставлен в %s" % marker.global_position)
