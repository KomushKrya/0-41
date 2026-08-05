extends SceneTree

## Renders scenes/main.tscn looking across the room at the window, so the cold
## night fill and the faintly glowing pane can be judged against the warm lamps.

const OUTPUT_PATH := "res://temp/window_side_view.png"
const WARMUP_FRAMES := 200

var _frames := 0
var _camera: Camera3D = null

func _initialize() -> void:
	get_root().add_child((load("res://scenes/main.tscn") as PackedScene).instantiate())

	_camera = Camera3D.new()
	_camera.name = "WindowSideShotCamera"
	_camera.fov = 70.0
	_camera.current = true
	# Stands near the door wall looking along -X straight at the window.
	_camera.transform = Transform3D(
		Vector3(0, 0, -1),
		Vector3(0, 1, 0),
		Vector3(1, 0, 0),
		Vector3(2.3, 1.45, 1.2)
	)
	get_root().add_child(_camera)

func _process(_delta: float) -> bool:
	_frames += 1
	if _frames < WARMUP_FRAMES:
		_camera.current = true
		return false
	var img := get_root().get_texture().get_image()
	img.save_png(OUTPUT_PATH)
	print("saved %s %dx%d" % [OUTPUT_PATH, img.get_width(), img.get_height()])
	return true
