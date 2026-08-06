extends SceneTree

# Снимок разворота досье с двумя сотрудниками, у каждого по два перка.
#
# Состав берётся из временного контента (по умолчанию res://temp/dossier_shot_data/),
# который готовит скрипт make_shot_data.py: два случайных человека уровня 3.
# Автозагрузка GameRuntime уже подняла ядро на боевом res://data — её заменяем
# своей копией, иначе на страницах окажется обычный стартовый состав.
#
# Снимать только без --headless: в headless рисует пустышка, и у вьюпорта нет текстуры.

const PAGE_SCENE := "res://scenes/ui/dossier/DossierUI.tscn"
const PAGE_WIDTH := 700

var _frames := 0
var _viewport: SubViewport
var _content_root := "res://temp/dossier_shot_data/"
var _out := "res://temp/dossier_perks.png"

func _initialize() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--content="):
			_content_root = arg.trim_prefix("--content=")
		elif arg.begins_with("--out="):
			_out = arg.trim_prefix("--out=")

	var autoloaded: Node = root.get_node_or_null("GameRuntime")
	if autoloaded != null:
		root.remove_child(autoloaded)
		autoloaded.queue_free()

	var runtime: Node = load("res://scripts/kontur/GameRuntime.cs").new()
	runtime.name = "GameRuntime"
	runtime.set("ContentRoot", _content_root)
	root.add_child(runtime)

	_viewport = SubViewport.new()
	_viewport.size = Vector2i(PAGE_WIDTH * 2, 900)
	_viewport.disable_3d = true
	_viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	root.add_child(_viewport)

	var page_scene: PackedScene = load(PAGE_SCENE)
	var left: Control = page_scene.instantiate()
	left.name = "DossierUI"
	var right: Control = page_scene.instantiate()
	right.name = "DossierUIRight"
	right.offset_left = PAGE_WIDTH
	right.offset_right = PAGE_WIDTH * 2
	_viewport.add_child(left)
	_viewport.add_child(right)

	# Разворот добавляется последним: его _Ready сразу тянет ростер из ядра.
	var spread: Node = load("res://scripts/dossier/DossierSpread.cs").new()
	spread.name = "DossierSpread"
	spread.set("LeftPagePath", NodePath("../DossierUI"))
	spread.set("RightPagePath", NodePath("../DossierUIRight"))
	_viewport.add_child(spread)

func _process(_delta: float) -> bool:
	_frames += 1
	if _frames < 30:
		return false
	var texture := _viewport.get_texture()
	if texture == null:
		push_error("У вьюпорта нет текстуры — снимок делается только с настоящим рендером, без --headless.")
		return true
	texture.get_image().save_png(_out)
	print("saved ", _out)
	return true
