extends SceneTree

## Записка, как она лежит на столе: камера над ней, сверху-сбоку.
## Нужен, чтобы смотреть на саму модель, а не на страницу перед камерой.

const OUTPUT_PATH := "res://temp/shift_note_desk.png"
const NOTE_PATH := "NewOffice/Interactable/ShiftNote"
const WARMUP_FRAMES := 150

var _frames := 0
var _scene: Node = null
var _camera: Camera3D = null

func _initialize() -> void:
	_scene = (load("res://scenes/main.tscn") as PackedScene).instantiate()
	get_root().add_child(_scene)
	_camera = Camera3D.new()
	_camera.name = "NoteShotCamera"
	_camera.fov = 40.0
	_camera.current = true
	get_root().add_child(_camera)

func _process(_delta: float) -> bool:
	_frames += 1
	var note := _scene.get_node_or_null(NOTE_PATH) as Node3D
	if note != null:
		var target := note.global_transform.origin
		_camera.global_transform = Transform3D(Basis.IDENTITY, target + Vector3(0.0, 0.22, 0.22)).looking_at(target, Vector3.UP)
		_camera.current = true
	if _frames < WARMUP_FRAMES:
		return false
	get_root().get_texture().get_image().save_png(OUTPUT_PATH)
	print("saved %s" % OUTPUT_PATH)
	return true
