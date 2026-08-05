extends SceneTree

# Снимок страницы блокнота. Значения шкал задаются через --fills=8,45,92
# (проценты заражение/гласность/лояльность); без аргумента берутся из ядра.

var _frames := 0
var _vp: SubViewport
var _ui: Node
var _fills: Array = []
var _out := "res://temp/notebook_page.png"

func _process(_delta: float) -> bool:
	_frames += 1
	if _frames == 1:
		for arg in OS.get_cmdline_user_args():
			if arg.begins_with("--fills="):
				for part in arg.trim_prefix("--fills=").split(","):
					_fills.append(float(part))
			elif arg.begins_with("--out="):
				_out = arg.trim_prefix("--out=")
		_vp = SubViewport.new()
		_vp.size = Vector2i(700, 990)
		_vp.disable_3d = true
		_vp.render_target_update_mode = SubViewport.UPDATE_ALWAYS
		_ui = load("res://scenes/ui/notebook/NotebookUI.tscn").instantiate()
		_vp.add_child(_ui)
		root.add_child(_vp)
		return false
	if _frames == 5 and not _fills.is_empty():
		# скрипт шкал перерисовывает заливку каждый кадр — глушим его и ставим свои значения
		_ui.set_process(false)
		var bars := _find_bars(_ui)
		for i in bars.size():
			var bar: Control = bars[i]
			var fill: Control = bar.get_node("MainFill")
			fill.position = Vector2.ZERO
			fill.size = Vector2(bar.size.x * _fills[i] / 100.0, bar.size.y)
			fill.visible = fill.size.x > 0.5
		return false
	if _frames < 12:
		return false
	_vp.get_texture().get_image().save_png(_out)
	print("saved ", _out)
	return true

func _find_bars(node: Node) -> Array:
	var found: Array = []
	if node.name == "AnimatedBar":
		found.append(node)
	for child in node.get_children():
		found.append_array(_find_bars(child))
	return found
