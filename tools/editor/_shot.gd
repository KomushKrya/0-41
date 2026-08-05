extends SceneTree

var _frames := 0
var _scene: Node = null

func _initialize() -> void:
	_scene = (load("res://scenes/main.tscn") as PackedScene).instantiate()
	get_root().add_child(_scene)

func _process(_delta: float) -> bool:
	_frames += 1
	if _frames < 150:
		return false
	var img := get_root().get_texture().get_image()
	img.save_png("res://temp/office_shot.png")
	print("saved temp/office_shot.png %dx%d" % [img.get_width(), img.get_height()])
	return true
