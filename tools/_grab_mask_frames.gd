extends SceneTree

# Одноразовый инструмент: снимает последний декодированный кадр каждой маски
# перехода и кладёт его в PNG. Запускать с окном (не --headless): в headless
# рендер-драйвер пустой, и обратное чтение текстуры видео не работает.

const OUT_DIR = "C:/Users/andy/AppData/Local/Temp/claude/C--Users-andy-Documents-GitHub-0-41/bd10b144-8dfc-4270-9e06-f5b1aab5c8da/scratchpad/mask_frames"

var files = [
	["res://assets/video/transition_masks/call/call_mask1.ogv", "call_mask1"],
	["res://assets/video/transition_masks/call/call_mask2.ogv", "call_mask2"],
	["res://assets/video/transition_masks/call/call_mask3.ogv", "call_mask3"],
	["res://assets/video/transition_masks/radio/radio_mask1.ogv", "radio_mask1"],
	["res://assets/video/transition_masks/radio/radio_mask2.ogv", "radio_mask2"],
	["res://assets/video/transition_masks/radio/radio_mask3.ogv", "radio_mask3"],
]

var idx = 0
var player: VideoStreamPlayer
var last_image: Image = null
var frames_seen = 0
var finished = false

func _initialize():
	DirAccess.make_dir_recursive_absolute(OUT_DIR)
	player = VideoStreamPlayer.new()
	player.expand = true
	player.custom_minimum_size = Vector2(1280, 720)
	root.add_child(player)
	player.finished.connect(_on_finished)
	_start_next()

func _start_next():
	if idx >= files.size():
		finished = true
		return
	last_image = null
	frames_seen = 0
	player.stream = load(files[idx][0])
	player.play()
	print("--- ", files[idx][1], " длительность: ", player.get_stream_length(), " с")

func _on_finished():
	_save()
	idx += 1
	_start_next()

func _save():
	var name = files[idx][1]
	if last_image == null:
		print("!!! ", name, ": кадр не получен")
		return
	var path = OUT_DIR + "/" + name + "_last.png"
	var err = last_image.save_png(path)
	print("OK  ", name, ": кадров ", frames_seen, ", ", last_image.get_width(), "x", last_image.get_height(), " -> ", path, " (err ", err, ")")

func _process(_delta):
	if finished:
		quit()
		return true
	var tex = player.get_video_texture()
	if tex != null:
		var img = tex.get_image()
		if img != null and img.get_width() > 0:
			last_image = img
			frames_seen += 1
	return false
