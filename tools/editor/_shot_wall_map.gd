extends SceneTree

## Renders the wall map in scenes/main.tscn head-on: the camera sits on the map's
## normal, so the board fills the frame without perspective skew.
##
## Board front face lives at x = 2.8734, spanning y 0.99..1.99 and z 1.384..2.756,
## with its normal pointing along -X into the room.

const OUTPUT_PATH := "res://temp/wall_map_view.png"
const WARMUP_FRAMES := 200

## Face centre of the board, and how far in front of it the camera sits.
const FACE_CENTRE := Vector3(2.8734, 1.4907, 2.0701)
const CAMERA_DISTANCE := 1.3

var _frames := 0
var _camera: Camera3D = null

func _initialize() -> void:
	get_root().add_child((load("res://scenes/main.tscn") as PackedScene).instantiate())

	_camera = Camera3D.new()
	_camera.name = "WallMapShotCamera"
	_camera.fov = 50.0
	_camera.current = true
	# Looking along +X (camera forward is local -Z), Y stays world up.
	_camera.transform = Transform3D(
		Vector3(0, 0, 1),
		Vector3(0, 1, 0),
		Vector3(-1, 0, 0),
		FACE_CENTRE - Vector3(CAMERA_DISTANCE, 0, 0)
	)
	get_root().add_child(_camera)

func _process(_delta: float) -> bool:
	_frames += 1
	if _frames < WARMUP_FRAMES:
		# The player rig claims the active camera as it settles, so keep ours current.
		_camera.current = true
		return false
	var img := get_root().get_texture().get_image()
	img.save_png(OUTPUT_PATH)
	print("saved %s %dx%d" % [OUTPUT_PATH, img.get_width(), img.get_height()])
	return true
