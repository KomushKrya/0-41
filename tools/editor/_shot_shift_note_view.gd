extends SceneTree

## Снимок кадра, который видит игрок, пока читает записку сменщика:
## сцена main.tscn, игрок сидит за столом, записка поднесена к камере.

## Записку другого дня и другой файл снимка задаём аргументами:
## --note=shift_note_day_4 --out=res://temp/shift_note_day_4.png

const NOTE_PATH := "NewOffice/Interactable/ShiftNote"
const UI_PATH := "NewOffice/Interactable/ShiftNote/ShiftNoteViewport/ShiftNoteUI"
const PLAYER_PATH := "NewOffice/PlayerCameraRig/CameraPitch/PlayerCamera"
const WARMUP_FRAMES := 180
const SETTLE_FRAMES := 90

var _output_path := "res://temp/shift_note_view.png"
var _note_id := ""
var _frames := 0
var _scene: Node = null
var _opened := false

func _initialize() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--note="):
			_note_id = arg.trim_prefix("--note=")
		elif arg.begins_with("--out="):
			_output_path = arg.trim_prefix("--out=")
	_scene = (load("res://scenes/main.tscn") as PackedScene).instantiate()
	get_root().add_child(_scene)

func _process(_delta: float) -> bool:
	_frames += 1
	if _frames < WARMUP_FRAMES:
		return false
	if not _opened:
		var note := _scene.get_node_or_null(NOTE_PATH)
		var player := _scene.get_node_or_null(PLAYER_PATH)
		if note == null or player == null:
			push_error("shift note or player not found: %s / %s" % [NOTE_PATH, PLAYER_PATH])
			return true
		note.call("OpenView", player)
		if _note_id != "":
			_scene.get_node(UI_PATH).call("Open", _note_id)
		_opened = true
		return false
	if _frames < WARMUP_FRAMES + SETTLE_FRAMES:
		return false
	var img := get_root().get_texture().get_image()
	img.save_png(_output_path)
	print("saved %s %dx%d" % [_output_path, img.get_width(), img.get_height()])
	return true
