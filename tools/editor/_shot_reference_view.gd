extends SceneTree

## Renders scenes/main.tscn from a fixed pose matching temp/office_shot reference framing:
## seated at the desk, looking along -Z at the far wall, wide enough to hold the
## desk lamp on the left and the radio plus wall map on the right.

const OUTPUT_PATH := "res://temp/office_reference_view.png"
const WARMUP_FRAMES := 180

var _frames := 0
var _scene: Node = null
var _camera: Camera3D = null

func _initialize() -> void:
	_scene = (load("res://scenes/main.tscn") as PackedScene).instantiate()
	get_root().add_child(_scene)

	_camera = Camera3D.new()
	_camera.name = "ReferenceShotCamera"
	_camera.fov = 68.0
	_camera.current = true
	_camera.transform = Transform3D(Basis.IDENTITY, Vector3(1.82, 1.30, 2.72))
	get_root().add_child(_camera)

func _process(_delta: float) -> bool:
	_frames += 1
	if _frames < WARMUP_FRAMES:
		# The player rig grabs the active camera while it settles into the seated
		# pose, so keep reasserting ours until the shot is taken.
		_camera.current = true
		return false
	var img := get_root().get_texture().get_image()
	img.save_png(OUTPUT_PATH)
	print("saved %s %dx%d" % [OUTPUT_PATH, img.get_width(), img.get_height()])
	return true
