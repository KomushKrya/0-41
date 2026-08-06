extends SceneTree

## Два снимка MapUI.tscn так, как его видит SubViewport настенной карты:
## с включённым отладочным слоем геометрии и без него.
##
## Запуск из корня репозитория:
##   godot --path . --script tools/editor/_shot_map_ui.gd

const OUTPUT_DIR := "res://temp/map_geometry"
const VIEWPORT_SIZE := Vector2i(944, 523)
const WARMUP_FRAMES := 6

## Имя файла и состояние отладочного слоя для каждого снимка.
const SHOTS := [
	{"file": "map_ui_debug_on.png", "debug": true},
	{"file": "map_ui_debug_off.png", "debug": false},
]

var _viewport: SubViewport = null
var _map_ui: Control = null
var _shot_index := 0
var _frames := 0

func _initialize() -> void:
	_viewport = SubViewport.new()
	_viewport.disable_3d = true
	_viewport.transparent_bg = false
	_viewport.size = VIEWPORT_SIZE
	_viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	get_root().add_child(_viewport)

	# Подложка кадра, а не карты: уводим её за все слои MapUI.
	var frame_background := ColorRect.new()
	frame_background.color = Color(0.12, 0.11, 0.10)
	frame_background.z_index = -100
	frame_background.size = Vector2(VIEWPORT_SIZE)
	_viewport.add_child(frame_background)

	_map_ui = (load("res://scenes/ui/map/MapUI.tscn") as PackedScene).instantiate() as Control
	_viewport.add_child(_map_ui)

	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(OUTPUT_DIR))

func _process(_delta: float) -> bool:
	_frames += 1
	# Переключаем слой только в кадре: на момент _initialize у MapBuildingEditor
	# ещё не отработал _Ready и его поля пустые.
	if _frames == 1:
		_apply_shot(SHOTS[_shot_index])

	if _frames < WARMUP_FRAMES:
		return false

	var shot: Dictionary = SHOTS[_shot_index]
	var path := "%s/%s" % [OUTPUT_DIR, shot["file"]]
	var image := _viewport.get_texture().get_image()
	var result := image.save_png(path)
	if result != OK:
		push_error("Не удалось сохранить %s: %s" % [path, result])
		return true

	print("saved %s %dx%d" % [path, image.get_width(), image.get_height()])

	_shot_index += 1
	if _shot_index >= SHOTS.size():
		return true

	_frames = 0
	return false

func _apply_shot(shot: Dictionary) -> void:
	# MapBuildingEditor — C#-класс, зовём метод по имени.
	_map_ui.call("SetLayoutDebugEnabled", shot["debug"])
